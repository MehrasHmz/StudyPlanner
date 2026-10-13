document.querySelectorAll('[data-session-include]').forEach(toggle => {
    const fields = toggle.closest('.session-result').querySelector('fieldset');
    const update = () => { fields.disabled = !toggle.checked; };
    toggle.addEventListener('change', update);
    update();
});
