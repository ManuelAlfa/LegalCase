// Descarga de un archivo servido por la Api.
//
// No sirve un <a href> normal: los endpoints exigen la cabecera
// Authorization con el JWT, que un enlace del navegador no envía. El archivo
// llega por el circuito de Blazor y aquí se convierte en descarga.
window.compasDescargas = {
    descargarArchivo: async (nombreArchivo, referenciaStream) => {
        const buffer = await referenciaStream.arrayBuffer();
        const url = URL.createObjectURL(new Blob([buffer]));
        const enlace = document.createElement('a');
        enlace.href = url;
        enlace.download = nombreArchivo ?? '';
        document.body.appendChild(enlace);
        enlace.click();
        enlace.remove();
        // Liberar la memoria del blob: sin esto queda retenida hasta recargar
        // la página, y un PDF escaneado pesa lo suyo.
        URL.revokeObjectURL(url);
    }
};
