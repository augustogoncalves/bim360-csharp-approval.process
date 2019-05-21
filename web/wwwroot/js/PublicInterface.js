
$(document).ready(function () {
    $('#uploadFile').click(function () {
        uploadFile();
    });

    $('#hiddenUploadField').change(function () {
        var _this = this;
        if (_this.files.length == 0) return;
        var file = _this.files[0];
        var formData = new FormData();
        formData.append('fileToUpload', file);
        formData.append('sessionId', connectionId);

        $.ajax({
            url: '/api/validation',
            data: formData,
            processData: false,
            contentType: false,
            type: 'POST',
            success: function (data) {

                _this.value = ''; // clear field
            }
        });
    });

    startConnection();
});

function uploadFile() {
    $('#hiddenUploadField').click();
}

var connection;
var connectionId;

function startConnection(onReady) {
    if (connection && connection.connectionState) { if (onReady) onReady(); return; }
    connection = new signalR.HubConnectionBuilder().withUrl("/api/signalr/validation").build();
    connection.start()
        .then(function () {
            connection.invoke('getConnectionId')
                .then(function (id) {
                    connectionId = id; // we'll need this...
                    if (onReady) onReady();
                });
        });

    connection.on("extractionFinished", function (data) {
        launchViewer(data.resourceUrn);
    });

    connection.on("validationFinished", function (data) {
        $('#validationList').empty();
        data.values.forEach(function (value) {
            switch (value.name.text.toLowerCase()) {
                case "proprietário": case "proprietario": case "local": case "contribuintes": case "zona de uso":
                    $('#validationList').append('<li class="list-group-item validationItem" onclick="zoomTo(\'' + value.value.handle + '\')">' + value.name.text + ': ' + value.value.text + ' <span class="badge"><span class="glyphicon glyphicon-ok"></span></span></li>')
                    break;
            }
        })

        data.areas.forEach(function (area) {
            if (area.name.text.toLowerCase().indexOf('não computável') > 1) return;
            if (area.name.text.toLowerCase().indexOf('computável') == -1) return;

            var handles = [];
            area.hatches.forEach(function (h) {
                handles.push(h.handle);
            })

            $('#validationList').append('<li class="list-group-item validationItem" onclick="zoomToM(\'' + area.name.handle + ',' + handles.join(',') + '\')">' + area.name.text + '<span class="badge"><span class="glyphicon glyphicon-ok"></span></span></li>')
        });
    });
}

function zoomTo(handle) {
    var viewer = viewerApp.myCurrentViewer;
    const utils = new Autodesk.Viewing.Utilities(viewer);

    viewer.search(handle, function (e) {
        viewer.select(e);
        viewer.utilities.fitToView(e);
    });
}

function zoomToM(handles) {
    var viewer = viewerApp.myCurrentViewer;
    const utils = new Autodesk.Viewing.Utilities(viewer);

    var ids = [];
    handles.split(',').forEach(function (handle) {
        viewer.search(handle, function (e) {
            ids.push(e[0])
            viewer.select(ids);
            viewer.utilities.fitToView(ids);
        }, null, ['Handle']);

    })
}