"""Parse untrusted uploads in a child process with no credentials and with limits (#90).

PDFium and python-docx run on bytes any tenant user uploads. In the service process they would sit
next to the blob connection string and app_ai, which reach every tenant. The child
(`parse_worker.py`) gets the file on stdin and an environment with only what Python needs to start,
and writes the sections to stdout as JSON. A wall-clock timeout kills it; on POSIX, RLIMIT_AS caps
its memory. A crash, hang, memory blow-up or output that is not the expected JSON fails the version
with `parse_error`, and the consumer moves on to the next message.

On Linux the process that runs the app makes itself non-dumpable at startup
(`protect_service_process`): a child of the same OS user can then not read that process's memory
or /proc/<pid>/environ. Only that one process: every ancestor holds the same environment and stays
readable, such as `uv run` and the `uvicorn --reload` supervisor (the AppHost runs both in
development). So the deploy must exec the venv's python or uvicorn directly, with no wrapper and
no reloader.

ponytail: the child still runs as the service's OS user, with the network, so code that exploits a
parser could still read an ancestor's /proc/<pid>/environ, `src/ai/.env`, or call out. The real fix
is deploy work (phase 3): run the child as a separate OS user, or in its own container, with no
network.
"""

import ctypes
import logging
import os
import subprocess
import sys
from collections.abc import Callable
from typing import TypedDict

from pydantic import TypeAdapter, ValidationError

from normetry_ai.ingestion.parsers import Parsed, Rejected
from normetry_ai.messaging.contracts import FailureReason

if sys.platform != "win32":
    import resource

logger = logging.getLogger(__name__)

# The content type is appended. -I: no PYTHON* variables, no user site-packages, no working
# directory on sys.path. The package is importable from the venv's site-packages.
COMMAND = [sys.executable, "-I", "-m", "normetry_ai.ingestion.parse_worker"]

# Everything else (NORMETRY_*, ConnectionStrings__*, URLs, keys) stays in the service process.
_KEEP = {"SYSTEMROOT", "PATH", "TEMP", "TMP"} if sys.platform == "win32" else {"PATH", "TMPDIR"}

_FAILED = "The file could not be read."


class _Rejection(TypedDict):
    reason: FailureReason
    text: str


class _RejectedOutput(TypedDict):
    rejected: _Rejection


# The child is untrusted once it has parsed the file: its output is validated like any input.
_OUTPUT: TypeAdapter[Parsed | _RejectedOutput] = TypeAdapter(Parsed | _RejectedOutput)


# <linux/prctl.h>
_PR_SET_DUMPABLE = 4


def protect_service_process() -> None:
    """Make this process non-dumpable on Linux (prctl PR_SET_DUMPABLE 0): its /proc/<pid> files
    then belong to root, so another process of the same user, such as a parsing child taken over
    by its file, can neither read its environment nor attach to it. This process only, never its
    parent (see the module docstring). A no-op elsewhere. Raises OSError when the call fails."""
    if sys.platform != "linux":
        return
    libc = ctypes.CDLL(None, use_errno=True)
    zero = ctypes.c_ulong(0)
    if libc.prctl(_PR_SET_DUMPABLE, zero, zero, zero, zero) != 0:
        raise OSError(ctypes.get_errno(), "prctl(PR_SET_DUMPABLE, 0) failed")


def child_environment() -> dict[str, str]:
    """The child's environment: only the variables Python needs to start."""
    return {name: value for name, value in os.environ.items() if name.upper() in _KEEP}


def _memory_limit(memory_mb: int) -> Callable[[], None] | None:
    if sys.platform == "win32":
        # ponytail: no memory limit on Windows (development only): a file that blows up a parser
        # can take the machine's memory until the timeout. Upgrade: a Job Object with
        # JOB_OBJECT_LIMIT_PROCESS_MEMORY around the child.
        return None
    limit = memory_mb * 1024 * 1024
    return lambda: resource.setrlimit(resource.RLIMIT_AS, (limit, limit))


def parse_in_subprocess(
    data: bytes, content_type: str, *, timeout_s: float, memory_mb: int
) -> Parsed:
    """`parse_file` in a child process: the same sections, or the same Rejected. A child that
    crashes, runs out of memory, outlives `timeout_s` or writes anything else raises
    Rejected("parse_error")."""
    try:
        child = subprocess.run(  # noqa: S603 (fixed argv; the content type is one argument)
            [*COMMAND, content_type],
            input=data,
            capture_output=True,
            timeout=timeout_s,
            env=child_environment(),
            # Runs in the forked child before exec: one system call, no imports or locks.
            preexec_fn=_memory_limit(memory_mb),
            check=False,
        )
    except subprocess.TimeoutExpired:
        # run() has killed the child. Its output can hold document text: never chained or logged.
        logger.warning("Parsing subprocess killed after %s seconds", timeout_s)
        raise Rejected("parse_error", "The file took too long to read.") from None
    if child.returncode != 0:
        logger.warning("Parsing subprocess exited with code %d", child.returncode)
        raise Rejected("parse_error", _FAILED)
    try:
        output = _OUTPUT.validate_json(child.stdout)
    except ValidationError:
        logger.warning("Parsing subprocess wrote unexpected output")
        raise Rejected("parse_error", _FAILED) from None
    if isinstance(output, Parsed):
        return output
    raise Rejected(output["rejected"]["reason"], output["rejected"]["text"])
