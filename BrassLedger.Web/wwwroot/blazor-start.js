window.brassLedgerMarkInteractive = () => {
    document.documentElement.dataset.blazorReady = "true";
    document.documentElement.dataset.blazorReadyUrl = window.location.href;
};

if (window.Blazor) {
    window.Blazor.start().then(() => {
        if (document.body.dataset.brassledgerRenderMode === "static") {
            window.brassLedgerMarkInteractive();
        }
    }).catch(error => {
        document.documentElement.dataset.blazorReady = "failed";
        console.error("Blazor failed to start", error);
    });
} else {
    document.documentElement.dataset.blazorReady = "failed";
    console.error("Blazor startup script is unavailable");
}
