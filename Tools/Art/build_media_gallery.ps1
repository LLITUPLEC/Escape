param([string]$ProjectRoot = (Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$output = Join-Path $ProjectRoot '.cache/qa/art'
$validation = Join-Path $output 'validation'
New-Item -ItemType Directory -Path $validation -Force | Out-Null
Add-Type -AssemblyName System.Drawing
$encoding = [System.Text.UTF8Encoding]::new($false)
$artRoot = 'Assets/_Project/Resources/Expedition'
$themes = @(
    @{ id='dawnwarden'; name='Капитан рассвета' },
    @{ id='voidranger'; name='Разведчик глубин' },
    @{ id='stormforged'; name='Мастер грозы' },
    @{ id='emberoracle'; name='Хранитель горна' },
    @{ id='frostwraith'; name='Полярный следопыт' },
    @{ id='phoenix'; name='Феникс' },
    @{ id='bookmaker'; name='Хозяин ставок' },
    @{ id='railwarden'; name='Страж магистрали' },
    @{ id='beastbreaker'; name='Укротитель глубин' },
    @{ id='alchemist'; name='Алхимик шахты' }
)
$slots = @(
    @{ id='helmet'; name='Шлем' }, @{ id='shoulders'; name='Наплечники' },
    @{ id='chest'; name='Нагрудник' }, @{ id='gloves'; name='Перчатки' },
    @{ id='legs'; name='Поножи' }, @{ id='feet'; name='Ботинки' },
    @{ id='weapon_l'; name='Левая рука' }, @{ id='weapon_r'; name='Правая рука' }
)
$assets = [System.Collections.Generic.List[string]]::new()
$heroCards = [System.Text.StringBuilder]::new()
$equipmentCards = [System.Text.StringBuilder]::new()
$monsterCards = [System.Text.StringBuilder]::new()
$utilityCards = [System.Text.StringBuilder]::new()
$heroContact = @()
$equipmentContact = @()
$monsterContact = @()
$utilityContact = @()
function ArtLink([string]$path, [string]$label, [string]$css='') {
    $full = Join-Path $ProjectRoot $path
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) { throw "Missing gallery asset: $path" }
    $assets.Add($path)
    $safe = [System.Net.WebUtility]::HtmlEncode($label)
    return "<a class='artlink $css' href='../../$path' data-title='$safe'><img loading='lazy' src='../../$path' alt='$safe'></a>"
}
foreach ($gender in @('male','female')) {
    $genderName = if ($gender -eq 'male') { 'Мужской образ' } else { 'Женский образ' }
    foreach ($theme in $themes) {
        $id = $theme.id + '-' + $gender
        $body = "$artRoot/Appearance/$id-body.png"
        $avatar = "$artRoot/Appearance/$id-avatar.png"
        $bodyLink = ArtLink $body ($theme.name + ' — ' + $genderName) 'body'
        $avatarLink = ArtLink $avatar ($theme.name + ' — аватарка') 'avatar'
        [void]$heroCards.AppendLine("<article class='card hero' data-search='$($theme.name) $genderName $id'><header><h3>$($theme.name)</h3><span>$genderName</span></header><div class='pair'>$bodyLink<div class='portrait'>$avatarLink<small>Парная аватарка</small></div></div><code>$id</code></article>")
        $heroContact += @{ path=$body; avatar=$avatar; label=$theme.name; detail=$genderName }
    }
}
foreach ($quality in @('epic','legendary')) {
    $qualityName = if ($quality -eq 'epic') { 'Эпический' } else { 'Легендарный' }
    foreach ($slot in $slots) {
        $id = "eq_t1_$($quality)_$($slot.id)"
        $path = "$artRoot/Items/$id.png"
        $link = ArtLink $path ($qualityName + ': ' + $slot.name)
        [void]$equipmentCards.AppendLine("<article class='card equipment $quality' data-search='$qualityName $($slot.name) $id'>$link<h3>$($slot.name)</h3><small>$qualityName · T1</small><code>$id</code></article>")
        $equipmentContact += @{ path=$path; label=$slot.name; detail=$qualityName }
    }
}
foreach ($floor in 5..12) {
    $path = "$artRoot/Monsters/mine-$floor.png"
    $link = ArtLink $path "Монстр шахты · этаж $floor"
    [void]$monsterCards.AppendLine("<article class='card monster' data-search='монстр шахта этаж $floor mine-$floor'>$link<h3>Этаж $floor</h3><code>mine_$floor</code></article>")
    $monsterContact += @{ path=$path; label="Этаж $floor"; detail="mine_$floor" }
}
$utilities = @(
    @{ path="$artRoot/Monsters/monster-frame-easy.png"; label='Лёгкий'; detail='Рамка монстра' },
    @{ path="$artRoot/Monsters/monster-frame-medium.png"; label='Средний'; detail='Рамка монстра' },
    @{ path="$artRoot/Monsters/monster-frame-hard.png"; label='Сложный'; detail='Рамка монстра' },
    @{ path="$artRoot/Monsters/monster-frame-boss.png"; label='Босс'; detail='Рамка монстра' },
    @{ path="$artRoot/Icons/friends.png"; label='Друзья'; detail='Иконка навигации' },
    @{ path="$artRoot/Items/supply-cache.png"; label='Ящик припасов'; detail='Нейтральная запасная иконка' }
)
foreach ($utility in $utilities) {
    $link = ArtLink $utility.path $utility.label
    [void]$utilityCards.AppendLine("<article class='card utility' data-search='$($utility.label) $($utility.detail)'>$link<h3>$($utility.label)</h3><small>$($utility.detail)</small></article>")
    $utilityContact += $utility
}

function DrawContained($graphics, [string]$path, [float]$x, [float]$y, [float]$width, [float]$height) {
    $source = [System.Drawing.Image]::FromFile((Join-Path $ProjectRoot $path))
    try {
        $scale = [Math]::Min($width / $source.Width, $height / $source.Height)
        $drawWidth = [float]($source.Width * $scale)
        $drawHeight = [float]($source.Height * $scale)
        $rect = [System.Drawing.RectangleF]::new(($x+($width-$drawWidth)/2), ($y+($height-$drawHeight)/2), $drawWidth, $drawHeight)
        $graphics.DrawImage($source, $rect)
    } finally { $source.Dispose() }
}
function ContactSheet($entries, [string]$filename, [int]$columns, [int]$cellWidth, [int]$cellHeight, [bool]$paired=$false) {
    $rows = [int][Math]::Ceiling($entries.Count / [double]$columns)
    $canvas = [System.Drawing.Bitmap]::new(($columns*$cellWidth), ($rows*$cellHeight))
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $font = [System.Drawing.Font]::new('Arial', 14, [System.Drawing.FontStyle]::Bold)
    $smallFont = [System.Drawing.Font]::new('Arial', 11)
    $ink = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(239,225,201))
    $muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(144,171,172))
    $panel = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(20,46,54))
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(8,25,30))
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        for ($i=0; $i -lt $entries.Count; $i++) {
            $entry = $entries[$i]
            $x = [int](($i % $columns)*$cellWidth)
            $y = [int]([Math]::Floor($i/[double]$columns)*$cellHeight)
            $graphics.FillRectangle($panel, ($x+5), ($y+5), ($cellWidth-10), ($cellHeight-10))
            $graphics.DrawString($entry.label, $font, $ink, [float]($x+14), [float]($y+14))
            $graphics.DrawString($entry.detail, $smallFont, $muted, [float]($x+14), [float]($y+38))
            if ($paired) {
                DrawContained $graphics $entry.path ($x+10) ($y+66) ($cellWidth-112) ($cellHeight-79)
                DrawContained $graphics $entry.avatar ($x+$cellWidth-110) ($y+98) 96 108
            } else {
                DrawContained $graphics $entry.path ($x+16) ($y+66) ($cellWidth-32) ($cellHeight-84)
            }
        }
        $canvas.Save((Join-Path $validation $filename), [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose(); $canvas.Dispose(); $font.Dispose(); $smallFont.Dispose(); $ink.Dispose(); $muted.Dispose(); $panel.Dispose()
    }
}
ContactSheet $heroContact 'contact-heroes.png' 5 300 400 $true
ContactSheet $equipmentContact 'contact-equipment.png' 4 270 310
ContactSheet $monsterContact 'contact-monsters.png' 4 300 340
ContactSheet $utilityContact 'contact-ui.png' 3 310 330

$screenshotCards = [System.Text.StringBuilder]::new()
$screenshotRoot = Join-Path $validation 'hero-ui'
$heroShots = @()
if (Test-Path -LiteralPath $screenshotRoot) {
    $heroShots = @(Get-ChildItem -LiteralPath $screenshotRoot -Filter '*.png' -Recurse | Where-Object { $_.BaseName -notlike 'hero-avatar-*' } | Sort-Object FullName)
    foreach ($file in $heroShots) {
        $relative = [System.IO.Path]::GetRelativePath($output, $file.FullName).Replace('\','/')
        $label = $file.Directory.Name + ' / ' + $file.BaseName
        [void]$screenshotCards.AppendLine("<article class='card screenshot' data-search='$label'><a class='artlink' href='$relative' data-title='$label'><img loading='lazy' src='$relative' alt='$label'></a><small>$label</small></article>")
    }
}
if ($screenshotCards.Length -eq 0) { [void]$screenshotCards.AppendLine("<p class='note'>Скриншоты добавятся после завершения проверки Unity.</p>") }
$fixCards = [System.Text.StringBuilder]::new()
$fixRoot = Join-Path $validation 'ui-fixes'
$fixShots = @(Get-ChildItem -LiteralPath $fixRoot -Filter '*.png' -ErrorAction SilentlyContinue | Sort-Object Name)
foreach ($file in $fixShots) {
    $relative = [System.IO.Path]::GetRelativePath($output, $file.FullName).Replace('\','/')
    $label = $file.BaseName
    [void]$fixCards.AppendLine("<article class='card screenshot' data-search='$label'><a class='artlink' href='$relative' data-title='$label'><img loading='lazy' src='$relative' alt='$label'></a><small>$label</small></article>")
}
$html = @'
<!doctype html>
<html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Escape · Галерея медиа</title>
<style>
:root{color-scheme:dark;--bg:#08191e;--panel:#142e36;--line:#285a60;--ink:#e9ddc8;--muted:#9fb4b2;--amber:#edaa53}*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 system-ui,sans-serif}main{max-width:1440px;margin:auto;padding:40px 24px 80px}h1{font:500 clamp(32px,5vw,58px)/1.1 Georgia,serif;margin:10px 0 16px}h2{font:500 30px Georgia,serif;margin:0}h3{font-size:15px;margin:0}p{max-width:850px;color:var(--muted)}a{color:var(--amber);text-decoration:none}a:hover{text-decoration:underline}.eyebrow{color:var(--amber);font-size:12px;letter-spacing:.16em;text-transform:uppercase}.stats{display:flex;gap:28px;flex-wrap:wrap;margin:24px 0}.stats strong{display:block;font:32px Georgia,serif;color:var(--ink)}.stats span{color:var(--muted);font-size:13px}nav{position:sticky;top:0;z-index:5;display:flex;flex-wrap:wrap;gap:8px;padding:12px 0;background:var(--bg);border-block:1px solid var(--line)}nav a,button{font:inherit;background:var(--panel);border:1px solid var(--line);padding:8px 14px;color:var(--ink);cursor:pointer;border-radius:5px}nav a:hover,button:hover{border-color:var(--amber);text-decoration:none}input{min-width:160px;flex:1;background:var(--panel);border:1px solid var(--line);border-radius:5px;color:var(--ink);padding:10px 12px;font:inherit}section{padding-top:42px;scroll-margin-top:92px}.section-head{display:flex;align-items:baseline;gap:20px;flex-wrap:wrap;margin-bottom:18px}.section-head a{font-size:13px}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}.heroes{grid-template-columns:repeat(5,minmax(0,1fr))}.card{border:1px solid var(--line);background:var(--panel);border-radius:8px;padding:14px;overflow:hidden}.card header{min-height:58px}.card header span,.card small{color:var(--muted);font-size:12px;display:block}code{display:block;color:var(--muted);font-size:10px;overflow-wrap:anywhere;margin-top:9px}.artlink{display:flex;align-items:center;justify-content:center;min-width:0;background:radial-gradient(ellipse at center,#234850 0%,#10262d 72%);border-radius:5px}.artlink img{display:block;max-width:100%;max-height:100%;object-fit:contain;transition:transform .2s}.artlink:hover img{transform:scale(1.025)}.equipment .artlink,.utility .artlink{height:230px;margin-bottom:14px}.monster .artlink{height:280px;margin-bottom:14px}.pair{height:320px;position:relative;display:flex;gap:4px;align-items:start}.pair .body{height:100%;flex:1}.portrait{width:65px;padding-top:10px;flex:0 0 65px}.portrait .avatar{height:76px}.portrait small{font-size:10px;margin-top:6px}.epic{border-top:3px solid #9e72c8}.legendary{border-top:3px solid var(--amber)}.utilities{grid-template-columns:repeat(6,minmax(0,1fr))}.utilities .artlink{height:165px}.screenshots{grid-template-columns:repeat(4,minmax(0,1fr))}.screenshot .artlink{height:380px;margin-bottom:10px}.note{border-left:2px solid var(--amber);padding-left:14px}.links{display:flex;gap:18px;flex-wrap:wrap;margin:14px 0;font-size:14px}footer{margin-top:48px;padding-top:20px;border-top:1px solid var(--line);font-size:13px;color:var(--muted)}dialog{padding:16px;border:1px solid var(--line);background:var(--bg);color:var(--ink);max-width:95vw;max-height:96vh;border-radius:8px}dialog::backdrop{background:#000c}dialog img{display:block;max-width:88vw;max-height:80vh;object-fit:contain;background:var(--panel);margin:12px auto}dialog header{display:flex;align-items:center;justify-content:space-between;gap:20px}dialog header span{font-size:13px}.light .artlink,.light dialog img{background:repeating-conic-gradient(#d8c5a2 0 25%,#bdac90 0 50%) 50%/20px 20px}.hidden{display:none!important}@media(max-width:1100px){.heroes{grid-template-columns:repeat(3,minmax(0,1fr))}.utilities{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:700px){main{padding:24px 14px}.grid,.heroes,.screenshots{grid-template-columns:repeat(2,minmax(0,1fr))}.pair{height:280px}.portrait{width:54px;flex-basis:54px}.equipment .artlink{height:170px}.monster .artlink{height:220px}nav{gap:5px}nav a,button{padding:7px 9px;font-size:13px}input{min-width:100%}.utilities .artlink{height:160px}.section-head{gap:10px}h2{font-size:26px}}
</style>
<main>
<div class="eyebrow">Escape · Экспедиция · 24 октября 2026</div>
<h1>Новые образы и снаряжение</h1>
<p>Полный набор из 70 созданных иллюстраций. Нажмите на изображение, чтобы рассмотреть оригинальный PNG; кнопка «Фон» показывает края и прозрачность.</p>
<div class="stats"><div><strong>20 + 20</strong><span>образов и парных аватарок</span></div><div><strong>16</strong><span>предметов экипировки</span></div><div><strong>8</strong><span>монстров шахты</span></div><div><strong>6</strong><span>рамок и служебных иконок</span></div></div>
<nav><a href="#heroes">Образы</a><a href="#equipment">Экипировка</a><a href="#monsters">Монстры</a><a href="#utility">Рамки и иконки</a><a href="#screenshots">Интерфейс</a><button id="background">Фон: тёмный</button><input id="search" type="search" placeholder="Поиск по имени или ID" aria-label="Поиск медиа"></nav>
<section id="heroes"><div class="section-head"><h2>Десять образов · две версии</h2><a href="validation/contact-heroes.png">Контактный лист</a><a href="appearance-unlock-conditions.txt">Условия открытия</a></div><div class="grid heroes">{{HEROES}}</div><p class="note">Аватарка всегда соответствует образу. В игре показываются десять вариантов для пола аккаунта: исходный доступен сразу, остальные открываются за достижения и игровой прогресс. Открытие и выбор сохраняются на сервере.</p></section>
<section id="equipment"><div class="section-head"><h2>Эпическая и легендарная экипировка</h2><a href="validation/contact-equipment.png">Контактный лист</a></div><div class="grid">{{EQUIPMENT}}</div><p>Каждому из 16 предметов назначен отдельный спрайт. Существующие изображения обычной и редкой экипировки, 32 рецептов и четырёх материалов сохранены.</p></section>
<section id="monsters"><div class="section-head"><h2>Шахта · этажи 5–12</h2><a href="validation/contact-monsters.png">Контактный лист</a></div><div class="grid">{{MONSTERS}}</div></section>
<section id="utility"><div class="section-head"><h2>Рамки и служебные иконки</h2><a href="validation/contact-ui.png">Контактный лист</a></div><div class="grid utilities">{{UTILITY}}</div></section>
<section id="screenshots"><div class="section-head"><h2>Проверка интерфейса на 1080 × 1920</h2></div><div class="links"><a href="validation/hero-ui/male/hero-skin-dawnwarden.png">Герой · мужская версия</a><a href="validation/hero-ui/female/hero-skin-dawnwarden.png">Герой · женская версия</a><a href="validation/hero-ui/registration/registration-female-selected.png">Выбор пола при регистрации</a><a href="validation/hero-ui/capture-report.json">Отчёт захвата</a></div><details><summary>Все скриншоты Героя и регистрации</summary><div class="grid screenshots">{{SCREENSHOTS}}</div></details></section>
<section id="fixes"><div class="section-head"><h2>Уведомления и взносы в турниры</h2></div><p>Уведомления проверены на реальных кадрах локального боя. Взносы проверены на локальных данных: реальные ставки и награды не отправлялись.</p><div class="links"><a href="validation/ui-fixes/achievement-toast-battle.png">Достижение во время боя</a><a href="validation/ui-fixes/stake-smith-purple.png">Взнос в кузнечный турнир</a><a href="validation/ui-fixes/stake-gold-2400.png">Взнос в турнир золота</a><a href="validation/ui-fixes/toast-play-validation.json">Проверка уведомлений</a><a href="validation/ui-fixes/stake-modal-validation.json">Проверка взносов</a></div><details><summary>Все скриншоты исправлений</summary><div class="grid screenshots">{{FIXSHOTS}}</div></details></section>
<footer><div class="links"><a href="media-audit-after.txt">Что ещё отсутствует · полный аудит</a><a href="implementation-notes.txt">Что изменилось</a><a href="validation/unity-media-validation.json">Проверка Unity</a><a href="validation/generated-png-validation.json">Проверка PNG</a><a href="hero-monster-media-manifest.json">Промпты образов и монстров</a><a href="extra-hero-media-manifest-037.json">Промпты новых пяти образов</a><a href="item-media-manifest.json">Промпты предметов</a></div>Создано встроенным image_gen. Галерея и контактные листы предназначены для просмотра; игра использует исходные PNG из Assets.</footer>
</main>
<dialog id="viewer"><header><span id="caption"></span><button id="close">Закрыть ×</button></header><img id="full" alt=""><a id="original" target="_blank" rel="noopener">Открыть исходный PNG</a></dialog>
<script>
const viewer=document.getElementById('viewer'),full=document.getElementById('full'),caption=document.getElementById('caption'),original=document.getElementById('original');
document.querySelectorAll('.artlink').forEach(link=>link.addEventListener('click',event=>{event.preventDefault();full.src=link.href;full.alt=link.dataset.title;caption.textContent=link.dataset.title;original.href=link.href;viewer.showModal()}));
document.getElementById('close').onclick=()=>viewer.close();viewer.addEventListener('click',event=>{if(event.target===viewer)viewer.close()});
document.getElementById('background').onclick=event=>{const light=document.body.classList.toggle('light');event.target.textContent=light?'Фон: клетка':'Фон: тёмный'};
document.getElementById('search').addEventListener('input',event=>{const query=event.target.value.trim().toLocaleLowerCase('ru');document.querySelectorAll('.card').forEach(card=>card.classList.toggle('hidden',!card.dataset.search.toLocaleLowerCase('ru').includes(query)))});
</script></html>
'@
$html = $html.Replace('{{HEROES}}',$heroCards.ToString()).Replace('{{EQUIPMENT}}',$equipmentCards.ToString()).Replace('{{MONSTERS}}',$monsterCards.ToString()).Replace('{{UTILITY}}',$utilityCards.ToString()).Replace('{{SCREENSHOTS}}',$screenshotCards.ToString()).Replace('{{FIXSHOTS}}',$fixCards.ToString())
[System.IO.File]::WriteAllText((Join-Path $output 'index.html'),$html,$encoding)
$result = @{ generatedOn='2026-10-24'; passed=($assets.Count -eq 70); uniqueAssets=(@($assets | Sort-Object -Unique).Count); totalAssetLinks=$assets.Count; contactSheets=4; assets=$assets.ToArray(); screenshots=($heroShots.Count+$fixShots.Count); heroScreenshots=$heroShots.Count; uiFixScreenshots=$fixShots.Count }
[System.IO.File]::WriteAllText((Join-Path $validation 'gallery-validation.json'),($result | ConvertTo-Json -Depth 6),$encoding)
if (!$result.passed) { throw "Expected 70 assets, got $($assets.Count)." }
Write-Output "Gallery complete: 70 assets, four contact sheets, $($result.screenshots) screenshots."
