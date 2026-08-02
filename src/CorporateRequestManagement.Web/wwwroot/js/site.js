// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
document.addEventListener("DOMContentLoaded", function () {
    const toggle = document.querySelector("[data-password-toggle]");
    if (toggle) toggle.addEventListener("click", function () { const input = document.getElementById("Password"); if (!input) return; const show = input.type === "password"; input.type = show ? "text" : "password"; toggle.textContent = show ? "Gizle" : "Göster"; });
    const form = document.querySelector("[data-login-form]");
    if (form) form.addEventListener("submit", function () { const button = form.querySelector("[data-login-submit]"); if (button) { button.disabled = true; button.setAttribute("data-kt-indicator", "on"); } });
});
