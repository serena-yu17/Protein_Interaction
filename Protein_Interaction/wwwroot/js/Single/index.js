function getRequestParam() {
    var urlParams = new URLSearchParams(window.location.search);
    if (urlParams.has("geneid") && urlParams.has("uplen") && urlParams.has("downlen") && urlParams.has("perlvl")) {
        var requestParam = {
            query: urlParams.get('geneid'),
            updepth: urlParams.get('uplen'),
            ddepth: urlParams.get('downlen'),
            width: urlParams.get('perlvl'),
            instanceID: instanceID
        };
        if (requestParam.query && requestParam.query !== '' && requestParam.query.length > 0 && requestParam.updepth >= 0 && requestParam.ddepth >= 0 && requestParam.width > 0)
            return requestParam;
        else
            return null;
    }
    return null;
}