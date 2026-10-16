function download(fileName, blob) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
}

export function downloadText(fileName, text, type = "text/plain") {
    download(fileName, new Blob([text], { type }));
}

export function downloadBytes(fileName, bytes, type = "application/octet-stream") {
    download(fileName, new Blob([bytes], { type }));
}
