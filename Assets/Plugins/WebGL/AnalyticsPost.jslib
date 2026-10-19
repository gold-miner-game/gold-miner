mergeInto(LibraryManager.library, {
  // A real form submit, not XHR. Google Forms does not send CORS headers,
  // so UnityWebRequest from WebGL never delivers the row.
  AnalyticsPostForm: function (urlPtr, bodyPtr) {
    var url = UTF8ToString(urlPtr);
    var body = UTF8ToString(bodyPtr);
    var frame = document.getElementById("analytics_frame");
    if (!frame) {
      frame = document.createElement("iframe");
      frame.name = "analytics_frame";
      frame.id = "analytics_frame";
      frame.style.display = "none";
      document.body.appendChild(frame);
    }
    var form = document.createElement("form");
    form.method = "POST";
    form.action = url;
    form.target = "analytics_frame";
    var parts = body.split("&");
    for (var i = 0; i < parts.length; i++) {
      var pair = parts[i];
      var cut = pair.indexOf("=");
      if (cut < 0) continue;
      var input = document.createElement("input");
      input.type = "hidden";
      input.name = decodeURIComponent(pair.substring(0, cut));
      input.value = decodeURIComponent(pair.substring(cut + 1).replace(/\+/g, " "));
      form.appendChild(input);
    }
    document.body.appendChild(form);
    form.submit();
    document.body.removeChild(form);
  }
});
