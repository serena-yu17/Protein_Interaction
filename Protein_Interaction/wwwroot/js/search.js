(function () {
    //Event handlers
    var refKey = '';

    var browserIsIE = null;
    var browserIsEdge = null;   

    function sliderZoom() {
        var slider = document.getElementById('inputscale');
        document.getElementById('scale').innerHTML = slider.value;
        var svg = document.getElementById('svggraph');
        if (svg === null || width === 0)
            return;
        svg.style.width = (width * slider.value / 100).toString() + "px";
        svg.style.height = (height * slider.value / 100).toString() + "px";
    }

    // JavaScript functions
    function initRefQuery() {
        document.getElementById("ref").classList.add('hidden');
        document.getElementById("spinner2").classList.remove('hidden');
        document.getElementById("outtext").innerHTML = '<p><br />Searching......<br /><br />If the query is too big, the search may take a long time.</p><br />';
    }

    function showRef(data) {
        var outtext = document.getElementById("outtext");
        if (data.length === 0) {
            outtext.innerHTML = 'No data found.';
            return;
        }

        var tableHtm = ['<table class="table table-responsive"><thead><tr><th>Upstream</th><th>Downstream</th><th>Reference</th></tr></thead><tbody>'];
        for (let i = 0; i < data.length; i++) {
            tableHtm.push('<tr>');
            tableHtm.push('<td>' + '<a href="https://www.ncbi.nlm.nih.gov/gene/' + data[i].gene1 + '" target="_blank">' + data[i].geneName1 + '</a></td>');
            tableHtm.push('<td>' + '<a href="https://www.ncbi.nlm.nih.gov/gene/' + data[i].gene2 + '" target="_blank">' + data[i].geneName2 + '</a></td>');
            var refArr = [];
            for (let j = 0; j < data[i].references.length; j++)
                refArr.push('<a href="https://www.ncbi.nlm.nih.gov/pubmed/' + data[i].references[j].refID + '" target="_blank">' + data[i].references[j].author + '</a>');
            var str = refArr.join(', ');
            tableHtm.push('<td scope="row">' + str + '</td>');
            tableHtm.push('</tr>');
        }
        tableHtm.push('</tbody></table>');
        document.getElementById('outtext').innerHTML = tableHtm.join('');
    }

    function saveimage() {
        setIE();
        setEdge();
        if (browserIsIE || browserIsEdge)  // If IE or Edge
        {
            alert("For MS IE and Edge browsers, please right-click on the picture and choose \"Save Picture As\".");
            return;
        }
        var svgGraph = document.getElementById('svggraph');
        var tempSvg = document.createElement('svg');
        tempSvg.setAttribute("version", "1.1");
        tempSvg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
        tempSvg.setAttribute("xmlns:xlink", "http://www.w3.org/1999/xlink");
        tempSvg.setAttribute("x", "0px");
        tempSvg.setAttribute("y", "0px");
        tempSvg.setAttribute("viewBox", "0 0 " + parseFloat(svgGraph.style.width).toFixed(1) + " " + parseFloat(svgGraph.style.height).toFixed(1));
        tempSvg.setAttribute("xml:space", "preserve");
        tempSvg.innerHTML = svgGraph.innerHTML;
        var anchor = document.createElement('a');
        anchor.setAttribute("href", "data:image/svg+xml;utf8," + tempSvg.outerHTML);
        anchor.setAttribute("download", "graph.svg");
        anchor.style.display = "none";
        anchor.setAttribute("target", "_blank");
        document.getElementById('outtext').appendChild(anchor);
        anchor.click();
        document.getElementById('outtext').removeChild(anchor);
    }

    function initquery() {
        document.getElementById("btnsubmit").disabled = true;
        document.getElementById("spinner").classList.remove('hidden');
        document.getElementById("ref").classList.add('hidden');
        document.getElementById("div-scale").classList.add('hidden');
        document.getElementById('prompt').innerHTML = '';
        document.getElementById('outtext').innerHTML = '';
        document.getElementById("spinner").innerHTML = document.getElementById('spinner').innerHTML;
        document.getElementById("output").innerHTML = '<p><br />Searching......<br /><br />If the query is too big, the search may take a long time.</p><br />';
    }

    function errorPrompt(information) {
        document.getElementById("div-scale").classList.add('hidden');
        document.getElementById('prompt').innerHTML = '';
        document.getElementById('output').innerHTML = '<br /><p>' + information + '</p>';
        document.getElementById('outtext').innerHTML = '';
    }

    function clearPrompt(information) {
        document.getElementById("div-scale").classList.add('hidden');
        document.getElementById('prompt').innerHTML = '';
        document.getElementById('output').innerHTML = '';
        document.getElementById('outtext').innerHTML = '';
    }

    function svgElem(type) {
        return document.createElementNS("http://www.w3.org/2000/svg", type);
    }

    function processJson(data) {
        if (data['status'] === 2) {
            errorPrompt('The figure is too large to render. Try to reduce length and number limits of your search');
            return [0, 0, []];
        }
        else if (data['status'] === 1) {
            errorPrompt('No data was found. The gene may not exist or may not be involved in any interactions recorded.');
            return [0, 0, []];
        }
        else {
            document.getElementById("ref").classList.remove('hidden');
            document.getElementById("div-scale").classList.add('hidden');
            document.getElementById("prompt").innerHTML = 'The search prioritises genes that have been reported more.<br />\
        Line Widths are proportional to numbers of academic reports.';
            return drawGraph(data);
        }
    }

    function setIE() {
        if (browserIsIE !== null)
            return;
        var ua = window.navigator.userAgent;
        var msie = ua.indexOf("MSIE ");

        if (ua.indexOf("MSIE") > 0 || ua.indexOf("Trident") > 0)
            browserIsIE = true;
        else
            browserIsIE = false;
    }

    function setEdge() {
        if (browserIsEdge !== null)
            return;
        if (window.navigator.userAgent.indexOf("Edge") > 0)
            browserIsEdge = true;
        else
            browserIsEdge = false;
    }

    function drawGraph(data) {
        var unit = 100;
        var radius = unit / 3.5;
        var width = data['xmax'] * unit + unit;
        var height = data['ymax'] * unit + unit;
        var nodeColors = {};
        document.getElementById("output").innerHTML = '<svg id="svggraph" class="mx-auto d-block">Sorry, your browser does not support inline SVG. Recommend using the newest version of Chrome.</svg >';
        var graph = document.getElementById("svggraph");
        var vb = ['0', '0', width.toString(), height.toString()];
        graph.setAttribute('viewBox', vb.join(' '));
        graph.setAttribute('width', width.toString());
        graph.setAttribute('height', height.toString());
        var vertex = data['vertex'];
        var edge = data['edge'];
        var group = null;
        // edges
        for (var i = 0; i < edge.length; i++) {
            if (edge[i][0] !== null && edge[i][1] !== null) {
                var x1 = vertex[edge[i][0]][1] * unit, x2 = vertex[edge[i][1]][1] * unit, y1 = vertex[edge[i][0]][2] * unit, y2 = vertex[edge[i][1]][2] * unit;
                lineWidth = Math.abs(edge[i][2]);
                if (lineWidth > 15)
                    lineWidth = 15;
                var arrowLen = 6 + lineWidth / 4;
                var arrowWidth = 3 + lineWidth * 0.7;
                var distance = Math.sqrt(Math.pow(x2 - x1, 2) + Math.pow(y2 - y1, 2));
                var vector = [(x2 - x1) / distance, (y2 - y1) / distance];
                var target = [x2 - vector[0] * radius * 1.1, y2 - vector[1] * radius * 1.1];
                var p2 = [x2 - vector[0] * (radius + arrowLen * 1.5), y2 - vector[1] * (radius + arrowLen * 1.5)];
                group = svgElem('g');
                var line = svgElem('line');
                line.setAttribute('x1', vector[0] * (radius + arrowLen) + x1);
                line.setAttribute('y1', vector[1] * (radius + arrowLen) + y1);
                line.setAttribute('x2', p2[0]);
                line.setAttribute('y2', p2[1]);
                line.style.strokeWidth = lineWidth;
                line.style.stroke = '#004D8B';
                line.style.strokeLinecap = 'round';
                group.appendChild(line);
                var arrow = svgElem('path');
                var m = target[0].toString() + ',' + target[1].toString();
                var l1 = (target[0] - vector[0] * arrowLen - vector[1] * arrowWidth).toString() + ',' + (target[1] - vector[1] * arrowLen + vector[0] * arrowWidth).toString();
                var l2 = (target[0] - vector[0] * arrowLen + vector[1] * arrowWidth).toString() + ',' + (target[1] - vector[1] * arrowLen - vector[0] * arrowWidth).toString();
                var pathStr = 'M' + m + ' L' + l1 + ' L' + l2 + ' Z';
                arrow.setAttributeNS(null, 'd', pathStr);
                arrow.style.fill = '#004D8B';
                group.appendChild(arrow);
                graph.appendChild(group);
            }
        }
        // vertex
        var query = new Set(data['query']);
        for (i = 0; i < vertex.length; i++) {
            var colorCode;
            if (nodeColors.hasOwnProperty(vertex[i][0]))
                colorCode = nodeColors[vertex[i][0]];
            else {
                color = [];
                var colorSum = 0;
                var baseColor = Math.random() * 120 + 80;
                for (var j = 0; j < 3; j++) {
                    color[j] = Math.random();
                    colorSum += color[j];
                }
                colorCode = '#' + Math.round(baseColor * color[0] / colorSum + (255 - baseColor)).toString(
                    16) + Math.round(baseColor * color[1] / colorSum + (255 - baseColor)).toString(16) + Math.round(baseColor * color[2] / colorSum + (255 - baseColor)).toString(16);
                nodeColors[vertex[i][0]] = colorCode;
            }
            var textColor = "#000000";
            group = svgElem('g');
            var circ = svgElem('circle');
            circ.setAttribute('cx', vertex[i][1] * unit);
            circ.setAttribute('cy', vertex[i][2] * unit);
            circ.setAttribute('r', radius);
            circ.style.strokeWidth = 1;
            circ.style.stroke = '#000000';
            circ.style.fill = colorCode;
            var fontSize = Math.max(2.3 * radius / vertex[i][0].length, 0.12 * 100);
            var txt = svgElem('text');
            txt.setAttribute('x', vertex[i][1] * 100);
            txt.setAttribute('y', vertex[i][2] * unit + fontSize / 2);
            txt.style.textAnchor = 'middle';
            txt.style.fill = textColor;
            txt.textContent = vertex[i][0];
            if (query.has(i)) {
                var ring = svgElem('circle');
                ring.setAttribute('cx', vertex[i][1] * unit);
                ring.setAttribute('cy', vertex[i][2] * unit);
                ring.setAttribute('r', radius + 5);
                ring.style.stroke = '#FFD700';
                ring.style.strokeWidth = '10';
                ring.style.fillOpacity = '0';
                ring.style.strokeOpacity = '0.5';
                group.appendChild(ring);
            }
            group.appendChild(circ);
            group.appendChild(txt);
            graph.appendChild(group);
        }
        //crop image and remove blank area
        var bbox = graph.getBBox();
        vb = [bbox.x - 5, bbox.y - 5, bbox.width + 10, bbox.height + 10];      //padding 5 to allow space for the golden rings.
        graph.setAttribute('viewBox', vb.join(' '));
        graph.setAttribute('width', vb[2] + 'px');
        graph.setAttribute('height', vb[3] + 'px');
        return [vb[2], vb[3]];
    }

    $('#btnref').click(function () {
        initRefQuery();
        var requestParam = {
            refKey: refKey
        };
        $.ajax({
            type: "GET",
            url: refURl,
            data: requestParam,
            success: function (response) {
                showRef(response);
            },
            error: function () {
                document.getElementById("outtext").innerHTML = 'No data found.';
            },
            complete: function () {
                document.getElementById("spinner2").classList.add('hidden');
                document.getElementById("ref").classList.add('hidden');
            }
        });
    });

    document.getElementById('inputscale').oninput = function () {
        setIE();
        if (!browserIsIE) {
            sliderZoom();
        }
    };

    document.getElementById('inputscale').onchange = function () {
        setIE();
        if (browserIsIE) {
            sliderZoom();
        }
    };

    $('#btnsave').click(function () {
        saveimage();
    });

    $(window).on("unload", function () {
        var xmlHttp = new XMLHttpRequest();
        xmlHttp.open("GET", cancelUrl, true);
        xmlHttp.send(instanceID);
    });

    var requestParam = getRequestParam();
    if (requestParam === null) {
        errorPrompt("Invalid input.");
    }
    else if (requestParam === undefined) {
        clearPrompt();
    }
    else if (requestParam !== null) {
        initquery();
        $.ajax({
            type: "GET",
            url: searchUrl,
            data: requestParam,
            success: function (response) {
                var output = processJson(response);
                width = output[0];
                height = output[1];
                refKey = response.refKey;
                document.getElementById('div-scale').classList.remove('hidden');
            },
            error: function () {
                errorPrompt('No data was found.');
            },
            complete: function () {
                document.getElementById("spinner").classList.add('hidden');
                document.getElementById("btnsubmit").disabled = false;
            }
        });
    }
})();
