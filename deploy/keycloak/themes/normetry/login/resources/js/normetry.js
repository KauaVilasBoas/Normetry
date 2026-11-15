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
    document.querySelectorAll('input[aria-invalid="true"]').forEach((input) => {
      input.setAttribute("aria-describedby", error.id);
    });
  }

  const form = document.getElementById("kc-form-login");
  if (!form) return;

  form.querySelectorAll("#username, #password").forEach((input) => {
    input.required = true;
  });

  form.addEventListener("submit", (event) => {
    if (event.defaultPrevented) return;
    form.setAttribute("aria-busy", "true");
    const submit = document.getElementById("kc-login");
    if (submit) {
      submit.setAttribute("aria-disabled", "true");
      submit.value = document.body.dataset.signingIn;
    }
  });
});
