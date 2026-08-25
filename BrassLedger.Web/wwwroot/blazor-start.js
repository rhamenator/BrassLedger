window.Blazor.start().then(() => {
    window.setTimeout(() => {
        document.documentElement.dataset.blazorReady = "true";
    }, 250);
});
