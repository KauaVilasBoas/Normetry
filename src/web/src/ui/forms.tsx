import { Check, ChevronDown, CircleAlert, FileText, Minus, Upload } from "lucide-react";
import { useId, useRef, useState, type ComponentProps, type DragEvent, type Ref } from "react";

/**
 * Native checkbox in a 44px label row; the description is its accessible description, followed
 * by whatever `aria-describedby` adds (a badge outside the row).
 */
export function Checkbox({
  label,
  description,
  "aria-describedby": describedBy,
  ...input
}: Omit<ComponentProps<"input">, "type"> & { label: string; description?: string }) {
  const id = useId();
  const describedByIds = [description && `${id}-description`, describedBy]
    .filter(Boolean)
    .join(" ");
  return (
    <label className="flex min-h-11 items-start gap-3 rounded-control px-2 py-2 hover:bg-elevated">
      <input
        type="checkbox"
        aria-labelledby={`${id}-label`}
        aria-describedby={describedByIds || undefined}
        className="mt-0.5 size-4 shrink-0 accent-mint"
        {...input}
      />
      <CheckboxText id={id} label={label} description={description} />
    </label>
  );
}

/**
 * A checkbox row that cannot change: a check or a dash, and `state` ("Granted:") for screen
 * readers. An unchecked row is dimmed unless `dimUnchecked` is false (an open action matters
 * as much as a done one).
 */
export function ReadOnlyCheckbox({
  checked,
  state,
  label,
  description,
  dimUnchecked = true,
}: {
  checked: boolean;
  state: string;
  label: string;
  description?: string;
  dimUnchecked?: boolean;
}) {
  const id = useId();
  const Icon = checked ? Check : Minus;
  return (
    <div className="flex min-h-11 items-start gap-3 px-2 py-2">
      <Icon
        aria-hidden="true"
        className={`mt-0.5 size-4 shrink-0 ${checked ? "text-text-primary" : "text-text-secondary"}`}
      />
      <CheckboxText
        id={id}
        state={state}
        label={label}
        description={description}
        muted={dimUnchecked && !checked}
      />
    </div>
  );
}

function CheckboxText({
  id,
  state,
  label,
  description,
  muted = false,
}: {
  id: string;
  state?: string;
  label: string;
  description?: string;
  muted?: boolean;
}) {
  return (
    <span className="min-w-0">
      <span
        id={`${id}-label`}
        className={`block text-body font-medium ${muted ? "text-text-secondary" : ""}`}
      >
        {state && <span className="sr-only">{state} </span>}
        {label}
      </span>
      {description && (
        <span id={`${id}-description`} className="block text-small text-text-secondary">
          {description}
        </span>
      )}
    </span>
  );
}

const fieldControl =
  "h-11 w-full rounded-control border border-teal-border bg-inset px-3 text-field sm:text-body";

/** A field's `aria-describedby` and `aria-invalid`, for its hint and error. */
function fieldAria(id: string, hint: string | undefined, error: string | undefined) {
  const describedBy = [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ");
  return {
    "aria-invalid": error ? (true as const) : undefined,
    "aria-describedby": describedBy || undefined,
  };
}

/** A field's hint, then its error (no red: an icon and white text). */
function FieldNotes({ id, hint, error }: { id: string; hint?: string; error?: string }) {
  return (
    <>
      {hint && (
        <p id={`${id}-hint`} className="text-small text-text-secondary">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-error`} className="flex items-start gap-1.5 text-small text-text-primary">
          <CircleAlert aria-hidden="true" className="size-4 shrink-0 text-text-secondary" />
          {error}
        </p>
      )}
    </>
  );
}

/** Labelled input with an optional hint and error. */
export function TextField({
  label,
  hint,
  error,
  ...input
}: ComponentProps<"input"> & { label: string; hint?: string; error?: string }) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <input id={id} {...fieldAria(id, hint, error)} className={fieldControl} {...input} />
      <FieldNotes id={id} hint={hint} error={error} />
    </div>
  );
}

/** The text field with a `<textarea>`: at least 88px tall, resizing vertically. */
export function TextAreaField({
  label,
  hint,
  error,
  ...textarea
}: ComponentProps<"textarea"> & { label: string; hint?: string; error?: string }) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <textarea
        id={id}
        {...fieldAria(id, hint, error)}
        className="min-h-22 w-full resize-y rounded-control border border-teal-border bg-inset px-3 py-2.5 text-field sm:text-body"
        {...textarea}
      />
      <FieldNotes id={id} hint={hint} error={error} />
    </div>
  );
}

/**
 * A compact filter in a card header (DESIGN.md Dropdown / select): a native select with no
 * visible label, so `label` names it.
 */
export function FilterSelect({
  label,
  options,
  ...select
}: Omit<ComponentProps<"select">, "children"> & {
  label: string;
  options: { value: string; label: string }[];
}) {
  return (
    <div className="relative w-full sm:w-auto">
      <select
        aria-label={label}
        className="h-9 w-full appearance-none rounded-sm border border-teal-border bg-inset pr-8 pl-2.5 text-small font-medium"
        {...select}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <ChevronDown
        aria-hidden="true"
        className="pointer-events-none absolute top-1/2 right-2.5 size-4 -translate-y-1/2 text-text-secondary"
      />
    </div>
  );
}

/**
 * A native select as a form field (DESIGN.md Form select): the text field's look with a chevron.
 * `placeholder` is the empty option, shown in secondary text and never chosen again.
 */
export function SelectField({
  label,
  placeholder,
  options,
  hint,
  error,
  ...select
}: Omit<ComponentProps<"select">, "children"> & {
  label: string;
  placeholder: string;
  options: { value: string; label: string }[];
  hint?: string;
  error?: string;
}) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-small text-text-primary">
        {label}
      </label>
      <div className="relative">
        <select
          id={id}
          {...fieldAria(id, hint, error)}
          className={`${fieldControl} appearance-none pr-10 *:text-text-primary ${select.value ? "" : "text-text-secondary"}`}
          {...select}
        >
          <option value="" disabled>
            {placeholder}
          </option>
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <ChevronDown
          aria-hidden="true"
          className="pointer-events-none absolute top-1/2 right-3 size-4 -translate-y-1/2 text-text-secondary"
        />
      </div>
      <FieldNotes id={id} hint={hint} error={error} />
    </div>
  );
}

const draggingFiles = (event: DragEvent) => event.dataTransfer.types.includes("Files");

/**
 * A file picker (DESIGN.md Drop zone): a native button, so Enter or Space opens the system
 * picker, that also takes a dropped file (a pointer shortcut). `onFiles` gets every file chosen
 * or dropped; the caller checks them. Its name is the line it shows; the hint and the error
 * describe it.
 */
export function DropZone({
  file,
  size,
  accept,
  label,
  draggingLabel,
  hint,
  another,
  error,
  onFiles,
  ref,
}: {
  file: File | undefined;
  /** The chosen file's size, formatted. */
  size: string;
  accept: string;
  label: string;
  draggingLabel: string;
  hint: string;
  another: string;
  error: string | undefined;
  onFiles: (files: File[]) => void;
  ref?: Ref<HTMLButtonElement>;
}) {
  const id = useId();
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const describedBy = [!file && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(" ");

  const over = (event: DragEvent) => {
    if (!draggingFiles(event)) return;
    event.preventDefault();
    setDragging(true);
  };

  return (
    <div className="flex flex-col gap-1.5">
      <button
        ref={ref}
        type="button"
        aria-labelledby={`${id}-label`}
        aria-describedby={describedBy || undefined}
        onClick={() => input.current?.click()}
        onDragEnter={over}
        onDragOver={over}
        onDragLeave={(event) => {
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false);
        }}
        onDrop={(event) => {
          if (!draggingFiles(event)) return;
          event.preventDefault();
          setDragging(false);
          onFiles([...event.dataTransfer.files]);
        }}
        className={`flex min-h-32 w-full flex-col items-center justify-center gap-1 rounded-control border p-4 text-center ${
          dragging
            ? "border-solid border-mint bg-teal-deep"
            : "border-dashed border-teal-border bg-inset hover:border-solid"
        }`}
      >
        {file && !dragging ? (
          <span id={`${id}-label`} className="flex w-full min-w-0 items-center gap-2 text-left">
            <FileText aria-hidden="true" className="size-5 shrink-0 text-text-secondary" />
            <span className="min-w-0 flex-1 truncate text-body font-medium">{file.name}</span>{" "}
            <span className="shrink-0 text-small text-text-secondary tabular-nums">{size}</span>{" "}
            <span className="shrink-0 text-small font-medium text-mint">{another}</span>
          </span>
        ) : (
          <>
            <Upload aria-hidden="true" className="size-5 text-text-secondary" />
            <span id={`${id}-label`} className="text-body font-medium">
              {dragging ? draggingLabel : label}
            </span>
            {!file && (
              <span id={`${id}-hint`} className="text-small text-text-secondary">
                {hint}
              </span>
            )}
          </>
        )}
      </button>
      <input
        ref={input}
        type="file"
        accept={accept}
        hidden
        tabIndex={-1}
        onChange={(event) => {
          onFiles([...(event.target.files ?? [])]);
          // The same file chosen again still counts.
          event.target.value = "";
        }}
      />
      <FieldNotes id={id} error={error} />
    </div>
  );
}
