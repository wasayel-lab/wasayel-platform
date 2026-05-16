window.ejarUi = {
    apply: function (theme, lang, dir) {
        var html = document.documentElement;
        html.setAttribute('lang', lang);
        html.setAttribute('dir', dir);
        if (theme === 'dark') document.body.classList.add('ac-dark');
        else document.body.classList.remove('ac-dark');
    }
};

window.ejarTz = {
    offset: function () { return new Date().getTimezoneOffset(); },
    name:   function () { return Intl.DateTimeFormat().resolvedOptions().timeZone ?? null; }
};
