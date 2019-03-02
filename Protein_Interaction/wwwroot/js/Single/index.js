function getRequestParam() {
    var urlParams = new URLSearchParams(window.location.search);
    var geneid = urlParams.get('geneid');
    var uplen = urlParams.get('uplen');
    var downlen = urlParams.get("downlen");
    var perlvl = urlParams.get('perlvl');
    if (geneid !== null && uplen !== null && downlen !== null && perlvl !== null) {
        var requestParam = {
            query: geneid,
            updepth: uplen,
            ddepth: downlen,
            width: perlvl,
            instanceID: instanceID
        };
        if (requestParam.query && requestParam.query !== '' && requestParam.query.length > 0 && requestParam.updepth >= 0 && requestParam.ddepth >= 0 && requestParam.width > 0) {
            document.getElementById("geneid").value = geneid;
            document.getElementById("uplen").value = uplen;
            document.getElementById("downlen").value = downlen;
            document.getElementById("perlvl").value = perlvl;
            return requestParam;
        }
        else
            return null;
    }
    return undefined;
}