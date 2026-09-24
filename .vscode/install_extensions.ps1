# Instalar todas las extensiones recomendadas en .vscode/extensions.json
Write-Host "Instalando extensiones recomendadas de VS Code..."

# Leer el JSON y extraer las recomendaciones
$extensions = (Get-Content ".vscode/extensions.json" | ConvertFrom-Json).recommendations

foreach ($ext in $extensions) {
    Write-Host "Instalando $ext..."
    code --install-extension $ext
}

Write-Host "✅ Instalación completa."