function getRequestParam() {
    var urlParams = new URLSearchParams(window.location.search);
    var geneList = urlParams.get('genelist');
    if (geneList) {
        var requestParam = {
            query: geneList,
            instanceID: instanceID
        };
        if (requestParam.query && requestParam.query !== '') {
            document.getElementById("genelist").value = geneList;
            return requestParam;
        }
        else
            return null;
    }
    return undefined;
}