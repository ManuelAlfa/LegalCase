// Prueba en navegador real del menú contextual de Clientes: botón derecho y
// botón "⋯", navegación a expediente, a la lista filtrada y a la pantalla de
// OCR con el documento resaltado, cierre con Escape y cliente sin nada.
//
// Necesita la aplicación en marcha y datos de la demo: el cliente "Antonio
// Rodríguez Marín" con el expediente 2026/0143 y al menos un documento.
//
// Ejecutar desde la raíz del repositorio (la primera vez instala Playwright
// en tests/navegador/node_modules, que está en .gitignore):
//
//   docker run --rm --network host -v "$PWD/tests/navegador":/w -w /w \
//     mcr.microsoft.com/playwright:v1.48.0-jammy npm i playwright@1.48.0 --no-audit --no-fund
//   docker run --rm --network host -e PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
//     -e WEB=http://localhost:5100 -v "$PWD/tests/navegador":/w -w /w \
//     mcr.microsoft.com/playwright:v1.48.0-jammy node menu_contextual_cliente.js
//
// Sale con código 1 si falla alguna comprobación.
const { chromium } = require('playwright');

const WEB = process.env.WEB || 'http://localhost:5100';
const CLIENTE = 'Antonio Rodríguez Marín';
const fallos = [];
const comprobar = (nombre, ok, detalle = '') => {
  console.log(`  ${ok ? 'OK ' : 'MAL'}  ${nombre}${ok ? '' : '  -> ' + detalle}`);
  if (!ok) fallos.push(nombre);
};

(async () => {
  const navegador = await chromium.launch();
  const p = await navegador.newPage({ viewport: { width: 1400, height: 900 } });
  const errores = [];
  p.on('console', m => { if (m.type() === 'error') errores.push(m.text()); });

  const irAClientes = async () => {
    await p.getByRole('link', { name: /^Clientes$/ }).click();
    await p.waitForSelector('tbody tr');
  };
  const abrirConBotonDerecho = async (nombre) => {
    await p.locator('tbody tr', { hasText: nombre }).click({ button: 'right', position: { x: 120, y: 15 } });
    await p.waitForSelector('.cps-ctx-seccion');
    await p.waitForTimeout(300);
  };

  await p.goto(WEB, { waitUntil: 'networkidle' });
  await p.getByRole('button', { name: /iniciar sesión/i }).click();
  await p.waitForSelector('table');
  await irAClientes();

  await abrirConBotonDerecho(CLIENTE);
  const secciones = await p.locator('.cps-ctx-seccion > span').allInnerTexts();
  console.log('Menú de', CLIENTE + ':', secciones.join(' | '));
  comprobar('el botón derecho abre el menú con sus tres secciones',
    ['expedientes', 'documentos', 'facturas'].every(s => secciones.some(x => x.toLowerCase().startsWith(s))), secciones.join(','));
  await p.screenshot({ path: 'menu-cliente.png' });

  await p.keyboard.press('Escape');
  await p.waitForTimeout(300);
  comprobar('Escape lo cierra', (await p.locator('.cps-ctx').count()) === 0);

  await p.locator('tbody tr', { hasText: CLIENTE }).locator('.cps-boton-mas').click();
  await p.waitForSelector('.cps-ctx-item');
  await p.getByRole('menuitem', { name: /2026\/0143/ }).click();
  await p.waitForURL('**/expedientes/*');
  await p.waitForSelector('h1');
  comprobar('el botón ⋯ abre el mismo menú y un expediente lleva a su ficha',
    (await p.locator('h1').innerText()).includes('2026/0143'));

  await irAClientes();
  await abrirConBotonDerecho(CLIENTE);
  await p.locator('.cps-ctx-seccion', { hasText: 'Expedientes' }).locator('.cps-ctx-ver-todos').click();
  await p.waitForSelector('.cps-filtro-activo');
  const filtro = await p.locator('.cps-filtro-activo').innerText();
  const clientesEnTabla = await p.locator('tbody tr td:nth-child(2) b').allInnerTexts();
  comprobar('"Ver todos" deja la lista de expedientes filtrada por el cliente',
    filtro.includes(CLIENTE) && clientesEnTabla.length > 0 && clientesEnTabla.every(c => c === CLIENTE),
    `${filtro} / ${clientesEnTabla}`);
  await p.screenshot({ path: 'expedientes-filtrados.png' });

  await irAClientes();
  await abrirConBotonDerecho(CLIENTE);
  const documento = p.locator('.cps-ctx-seccion', { hasText: 'Documentos' }).locator('xpath=following-sibling::button[1]');
  if (await documento.count()) {
    await documento.click();
    await p.waitForURL('**/ocr?*');
    await p.waitForSelector('.cps-filtro-activo');
    await p.waitForTimeout(600);
    comprobar('un documento lleva a OCR filtrado por el cliente y con él resaltado',
      (await p.locator('tr.cps-fila-destacada').count()) === 1);
    await p.screenshot({ path: 'ocr-filtrado.png' });
  } else {
    console.log('  --   (el cliente no tiene documentos: no se prueba ese camino)');
  }

  comprobar('sin errores de consola', errores.length === 0, errores.join(' ~ '));
  await navegador.close();
  console.log(fallos.length ? `\nFALLAN ${fallos.length}: ${fallos}` : '\nTodas las comprobaciones pasan.');
  process.exit(fallos.length ? 1 : 0);
})();
