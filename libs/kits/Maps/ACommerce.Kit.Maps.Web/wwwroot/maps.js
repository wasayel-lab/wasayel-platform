// AcLocationInput — يَملأ حَقلَي lat/lng لِـ {Name} مِن navigator.geolocation.
// يُحَمَّل عَبر <script src="_content/ACommerce.Kit.Maps.Web/maps.js" defer></script>.
window.acLocFill = function (name) {
    if (!navigator.geolocation) {
        alert('المُتَصَفِّح لا يَدعَم تَحديد المَوقِع.');
        return;
    }
    var lat = document.getElementById(name + '_lat');
    var lng = document.getElementById(name + '_lng');
    if (!lat || !lng) return;
    var prev = { lat: lat.value, lng: lng.value };
    lat.value = '...'; lng.value = '...';
    navigator.geolocation.getCurrentPosition(
        function (p) {
            lat.value = p.coords.latitude.toFixed(6);
            lng.value = p.coords.longitude.toFixed(6);
        },
        function (e) {
            lat.value = prev.lat; lng.value = prev.lng;
            alert('تَعَذَّر تَحديد المَوقِع: ' + e.message);
        },
        { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 }
    );
};
