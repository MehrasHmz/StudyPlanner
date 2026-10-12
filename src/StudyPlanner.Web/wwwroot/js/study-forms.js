(() => {
    'use strict';
    document.querySelectorAll('[data-count-for]').forEach(output => {
        const input = document.getElementById(output.dataset.countFor);
        if (!input) return;
        const update = () => { output.textContent = input.value.length + ' / ' + input.maxLength; };
        input.addEventListener('input', update); update();
    });
    const duration = document.getElementById('EstimatedDurationMinutes');
    const presets = [...document.querySelectorAll('[data-minutes]')];
    const updateDuration = () => presets.forEach(button => button.setAttribute('aria-pressed', String(Number(button.dataset.minutes) === Number(duration.value))));
    if (duration) {
        duration.addEventListener('input', updateDuration);
        presets.forEach(button => button.addEventListener('click', () => { duration.value = button.dataset.minutes; duration.dispatchEvent(new Event('input', { bubbles: true })); }));
        updateDuration();
    }
    const review = document.getElementById('NextReviewDate');
    document.querySelectorAll('[data-review-days]').forEach(button => button.addEventListener('click', () => {
        if (!review) return;
        if (button.dataset.reviewDays === '') review.value = '';
        else {
            const date = new Date(); date.setDate(date.getDate() + Number(button.dataset.reviewDays));
            review.value = date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
        }
        review.dispatchEvent(new Event('input', { bubbles: true }));
    }));
})();
