
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

    });
}