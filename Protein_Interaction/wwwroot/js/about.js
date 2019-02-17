(function () {
    $.ajax({
        type: 'GET',
        url: countUrl,
        success: function (data) {
            document.getElementById('gene-count').innerHTML = data.id;
            document.getElementById('syn-count').innerHTML = data.syn;
            document.getElementById('record-count').innerHTML = data.nref;
        }
    });
})();