// Enhance the inherited form without handling credentials or changing its submission.
document.addEventListener("DOMContentLoaded", () => {
  // The inherited templates use positive tab indices; use the visual DOM order instead.
  document.querySelectorAll("[tabindex]").forEach((element) => {
    if (Number(element.getAttribute("tabindex")) > 0) {
      element.setAttribute("tabindex", "0");
    }
  });

  const error = document.getElementById("input-error");
  if (error) {
    const invalid = document.querySelectorAll('input[aria-invalid="true"]');
    invalid.forEach((input) => {
      input.setAttribute("aria-describedby", error.id);
    });
    // Autofocus ran before the description existed; focus again so it is announced.
    invalid[0]?.blur();
    invalid[0]?.focus();
  }

  const form = document.getElementById("kc-form-login");
  if (!form) return;

  form.querySelectorAll("#username, #password").forEach((input) => {
    input.required = true;
  });

  // The live region must exist before its text changes.
  const status = document.createElement("p");
  status.className = "sr-only";
  status.setAttribute("role", "status");
  form.after(status);

  form.addEventListener("submit", (event) => {
    if (event.defaultPrevented) return;
    form.setAttribute("aria-busy", "true");
    status.textContent = document.body.dataset.signingIn;
    const submit = document.getElementById("kc-login");
    if (submit) {
      submit.setAttribute("aria-disabled", "true");
      submit.value = document.body.dataset.signingIn;
    }
  });
});
