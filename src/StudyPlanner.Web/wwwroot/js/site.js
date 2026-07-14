// StudyPlanner - Site JavaScript

(function () {
    'use strict';

    // ===== Debounce utility =====
    function debounce(fn, ms) {
        let timer;
        return function () {
            clearTimeout(timer);
            timer = setTimeout(fn, ms);
        };
    }

    // ===== Navbar scroll effect =====
    function initNavbar() {
        const nav = document.getElementById('mainNav');
        if (!nav) return;
        const handleScroll = () => {
            nav.classList.toggle('scrolled', window.scrollY > 50);
        };
        window.addEventListener('scroll', handleScroll, { passive: true });
        handleScroll();
    }

    // ===== Intersection Observer for card animations =====
    function initCardAnimations() {
        const observerOptions = {
            threshold: 0.05,
            rootMargin: '0px 0px -30px 0px'
        };

        const fadeObserver = new IntersectionObserver((entries) => {
            entries.forEach((entry, index) => {
                if (entry.isIntersecting) {
                    const delay = Math.min(index * 60, 300);
                    setTimeout(() => {
                        entry.target.style.opacity = '1';
                        entry.target.style.transform = 'translateY(0)';
                    }, delay);
                    fadeObserver.unobserve(entry.target);
                }
            });
        }, observerOptions);

        document.querySelectorAll('.card, .stat-card').forEach((el) => {
            el.style.opacity = '0';
            el.style.transform = 'translateY(15px)';
            el.style.transition = 'opacity 0.4s ease, transform 0.4s ease';
            fadeObserver.observe(el);
        });
    }

    // ===== Auto-dismiss alerts =====
    function initAlerts() {
        document.querySelectorAll('.alert-dismissible').forEach(alert => {
            setTimeout(() => {
                const closeBtn = alert.querySelector('.btn-close');
                if (closeBtn) closeBtn.click();
            }, 5000);
        });
    }

    // ===== Form focus effects =====
    function initFormEffects() {
        document.querySelectorAll('.form-control, .form-select').forEach(input => {
            input.addEventListener('focus', function () {
                this.parentElement?.classList.add('input-focused');
            });
            input.addEventListener('blur', function () {
                this.parentElement?.classList.remove('input-focused');
            });
        });
    }

    // ===== Page load animation =====
    function initPageLoad() {
        document.body.style.opacity = '0';
        document.body.style.transition = 'opacity 0.25s ease';
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                document.body.style.opacity = '1';
            });
        });
    }

    // ===== Initialize everything =====
    function init() {
        initNavbar();
        initCardAnimations();
        initAlerts();
        initFormEffects();
        initPageLoad();
    }

    // Run on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
