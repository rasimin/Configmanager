// ConfigManager Desktop Helper Functions
window.ConfigManager = {
    copyToClipboard: function (text) {
        if (navigator.clipboard) {
            return navigator.clipboard.writeText(text);
        } else {
            var textarea = document.createElement("textarea");
            textarea.value = text;
            document.body.appendChild(textarea);
            textarea.select();
            document.execCommand("copy");
            document.body.removeChild(textarea);
            return Promise.resolve();
        }
    }
};
