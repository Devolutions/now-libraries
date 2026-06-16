# NOTE: Markdown Monster CLI may be broken at the moment (WinGet version 3.5.4 definitely is),
# the PDF could be generated manually via GUI instead.
$SpecPath = Join-Path $PSScriptRoot 'NOW-spec.md'
$OutputPath = Join-Path $PSScriptRoot 'NOW-spec.pdf'

mmcli markdowntopdf -i $SpecPath -o $OutputPath --theme Github --orientation Portrait --page-size A4