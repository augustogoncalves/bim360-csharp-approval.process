
$(document).ready(function () {
    $('#uploadFile').click(function () {
        uploadFile();
    });

    $('#sendProject').click(function () {
        var file = $('#hiddenUploadField')[0].files[0];

        var formData = new FormData();
        formData.append('fileToUpload', file);
        formData.append('projectNumer', dataToSubmit.projectNumber);
        formData.append('phoneNumber', $('#phone').val());

        $.ajax({
            url: '/api/submit',
            data: formData,
            processData: false,
            contentType: false,
            type: 'POST',
            success: function (data) {
                //_this.value = ''; // clear field
            }
        });


        /* $.ajax({
             url: '/api/submit',
             type: 'post',
             dataType: 'json',
             contentType: 'application/json',
             success: function (data) {
             },
             data: JSON.stringify(dataToSubmit)
         });;*/
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
                //_this.value = ''; // clear field
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
                    if (id.indexOf('_') !== -1) {
                        console.log('Restarting...');
                        connection.stop(); // need to fix this..
                        connection = null;
                        startConnection();
                        return;
                    }
                    connectionId = id; // we'll need this...
                    if (onReady) onReady();
                });
        });

    connection.on("extractionFinished", function (data) {
        launchViewer(data.resourceUrn);
    });

    connection.on("validationFinished", function (fileName, data) {
        $('#validationList').empty();
        data.values.forEach(function (value) {
            switch (value.name.text.toLowerCase()) {
                case "contribuintes":
                    dataToSubmit['projectNumber'] = value.value.text;
                case "proprietário": case "proprietario": case "local": case "zona de uso":
                    $('#validationList').append('<li class="list-group-item validationItem" onclick="zoomTo(\'' + value.value.handle + '\')">' + value.name.text + ': ' + value.value.text + ' <span class="badge"><span class="glyphicon glyphicon-ok"></span></span></li>')
                    break;
            }
        })

        var check = false;
        data.areas.forEach(function (area) {
            if (area.name.text.toLowerCase().indexOf('não computável') > 1) return;
            if (area.name.text.toLowerCase().indexOf('computável') == -1) return;

            if (area.name.text.toLowerCase().indexOf('permeável') > -1) check = true;

            var handles = [];
            var areaTotal = 0.0;
            area.hatches.forEach(function (h) {
                handles.push(h.handle);
                areaTotal += h.area;
            })

            $('#validationList').append('<li class="list-group-item validationItem" onclick="zoomToM(\'' + handles.join(',') + '\')">' + area.name.text + ' (' + Math.round(areaTotal / 10) / 100 + 'm<sup>2</sup>)<span class="badge"><span class="glyphicon glyphicon-ok"></span></span></li>')
        });

        // hack to just check this information
        if (!check) {
            $('#validationList').append('<li class="list-group-item list-group-item-danger validationItem">ÁREA PERMEÁVEL - COMPUTÁVEL<span class="badge"><span class="glyphicon glyphicon-remove"></span></span></li>')
        }
        else {
            dataToSubmit['fileName'] = fileName;
            $('#readyToSubmit').show();
        }
    });
}

var dataToSubmit = {};

function zoomTo(handle) {
    var viewer = viewerApp.myCurrentViewer;

    viewer.search(handle, function (e) {
        viewer.isolate(e);
        viewer.select(e);
    });
}

function zoomToM(handles) {
    var viewer = viewerApp.myCurrentViewer;

    var ids = [];
    if (handles.length == 0) {
        viewer.isolate(0);
        viewer.select();
        viewer.utilities.goHome();
    }
    else {
        handles.split(',').forEach(function (handle) {
            viewer.search(handle, function (e) {
                ids.push(e[0])
                viewer.isolate(ids);
                viewer.select(ids);
            }, null, ['Handle']);

        })
    }
}