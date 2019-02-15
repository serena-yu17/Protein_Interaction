function getRequestParam() {
    var urlParams = new URLSearchParams(window.location.search);
    if (urlParams.has("genelist")) {
        var requestParam = {
            query: urlParams.get('genelist'),
            instanceID: instanceID
        };
        if (requestParam.query && requestParam.query !== '')
            return requestParam;
        else
            return null;
    }
    return null;
}