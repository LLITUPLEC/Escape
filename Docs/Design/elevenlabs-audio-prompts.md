# Звуки Escape — промпты для ElevenLabs

Звук Escape — это снаряжение экспедиции, которое слышно в каменном помещении. На поле сталкиваются пластик канистры, латунь компаса, ткань рюкзака, твёрдая капсула и корпус бомбы. В интерфейсе работают пружины, защёлки и рычаги. Музыкальные акценты звучат в начале боя, при дополнительном ходе и в финале матча. Частые звуки короткие и с небольшим послезвучием.

**Сохранить** — ярость `Bloodlust_cast.ogg`, щит `Heroism_Cast.ogg` и турнирный тик `tik_timer.wav`. Звук истечения времени самого хода — отдельный сигнал, для него есть промпт.

## Как генерировать и выбирать

1. В ElevenLabs открой **Playground → Sound Effects**. Для этих промптов подходит **SFX v3**, в том числе для гонга и вокального сигнала. Начни с **Prompt Influence 30%**, для точной фонетики попробуй **45–55%**. Для коротких сигналов выставь 0,5–0,7 с и после скачивания подрежь пустоту и лишний хвост. Для гонга сразу задай **1,8 с**. У всех разовых эффектов **Looping выключен**. Экспортируй WAV, если он доступен для выбранной генерации. [Настройки Sound Effects](https://elevenlabs.io/docs/eleven-creative/playground/sound-effects) · [Модель SFX v3](https://elevenlabs.io/docs/overview/capabilities/sound-effects)
2. Начни с **разрушения линии и приземления каскада**. Из «Небес» здесь берём идею короткой россыпи, в которой каждый контакт слышен. В Escape материал задают сами фишки. Линия исчезает с одним сухим щелчком, а при падении слышна череда глухих стуков с коротким латунным щелчком. Прослушай их подряд на записи 2–3 каскадов. «Крест» и «Квадрат» проверяй в наложении на первый звук линии, как они срабатывают в игре. Звук приземления сейчас срабатывает один раз на каскад, поэтому короткая россыпь заложена в один файл.
3. Сравни варианты при примерно одинаковой воспринимаемой громкости, сначала на динамике телефона. Падение и кнопки должны оставаться тихими. Критический удар — чуть выразительнее обычного. Проверяй, что второй акцент дополнительного хода слышен, когда оба сигнала совпадают. Для гонга оставь чистую атаку и короткий хвост. Сохраняй файл под именем из заголовка.

Условия Sound Effects разрешают использовать эффекты в составе игры при подходящем тарифе и ограничивают публикацию отдельных аудиофайлов. Это важно для страницы прослушивания Escape с отдельными WAV. [Условия ElevenCreative, Exhibit 4](https://elevenlabs.io/creative-terms)

## Бой

Длительности под заголовками — ориентиры для **готового клипа** после подрезки. Вставляй каждый английский блок отдельной генерацией. Имена файлов соответствуют текущим клипам в `Assets/_Project/Audio/SFX/Match3/`.

### Разрушение линии · `m3_line_clear.wav`

**0,18–0,30 с.** Событие повторяется в каждом каскаде. Атака сухая и мгновенная, хвост короткий.

```text
Mobile game one-shot for a matched row of expedition gear disappearing. Immediate tight brass latch snap with a dry plastic knock and a tiny canvas brush that cuts away fast. Close mic, short stone-room reflection. Clean, tactile and repeatable. No glass break, explosion, melodic notes or voice.
```

### Приземление каскада · `fall.wav`

**0,26–0,42 с.** Первая фишка слышна сразу. Остальные контакты короткие, с небольшими разными интервалами и массой.

```text
Close-miked cascade of small expedition gear landing on worn slate. Start with a hollow plastic canister knock, then stagger a short brass compass case click and a soft canvas backpack thump. A tiny stone skid and a quick stop. Light, dry, each contact distinct. No glass, ringing chimes, falling debris or explosion.
```

### «Крест» · `m3_ability_cross.wav`

**0,18–0,30 с.** Импульс из центра в стороны, слышимый поверх разрушения линии.

```text
Very short magical fault opening in a slate floor. A low dry snap starts at the center, with two thin pressure scrapes rushing outward and stopping together. Tight midrange, a trace of stone dust, close perspective. Leave room for a separate gear-clearing click. No glass, explosion, sustained hiss or voice.
```

### «Квадрат» · `m3_ability_square.wav`

**0,24–0,38 с.** Упругий щелчок и короткий пробег по замкнутому контуру.

```text
One rectangular pressure seal snapping shut around a slate panel. A dry, slightly raspy scrape traces the perimeter and ends in a compact iron latch click. Grounded, warm midrange, very short decay. Leave room for a separate gear-clearing click. No glass, gong, explosion or voice.
```

### «Петарда» · `m3_ability_petard.wav`

**0,25–0,40 с.** Один короткий хлопок с лёгкой отдачей помещения.

```text
One small mining firecracker pops sharply in a narrow stone chamber. Tight paper-like crack, a short puff and a few specks of grit tapping the floor. Immediate attack, close mic, quickly damped. No fuse lead-in, gunshot tail, large explosion, music or voice.
```

### Обычное попадание · `m3_damage_hit.wav`

**0,16–0,28 с.** Матовый удар по снаряжению, читаемый без тяжёлого баса.

```text
One short combat hit on a miner's padded leather armor with a metal buckle. A compact dense thud, a faint dry buckle tick and almost no tail. Close perspective, clear midrange for phone speakers. No gore, scream, glass, deep boom or dramatic echo.
```

### Критическое попадание · `m3_damage_crit.wav`

**0,22–0,38 с.** Та же броня, более чёткая атака и короткий металлический акцент. Этот файл звучит вместо обычного удара.

```text
One decisive critical hit on padded miner armor. Compact leather thud with a bright, very short forged-steel edge on the same impact, then quick damping. Keep it close to the loudness of a regular hit, just more defined. No second strike, long ring, bass drop, scream or cinematic boom.
```

### Дополнительный ход за 5+ · `5+.wav`

**Около 0,65 с.** Короткое инструментальное «та-даа». За низким вступлением идёт более высокий аккорд медных. Два акцента должны хорошо различаться даже поверх критического попадания.

```text
Two-note heroic extra-turn reward sting, like a punchy instrumental ta-DAAA. A short lower muted brass pickup at the very start, then a bright, higher French-horn major chord at 0.11 seconds. Compact, warm, adventurous fantasy, close and clean with a soft stop under 0.65 seconds. No vocals, speech, cymbal, coins or long reverb.
```

Вариант с тембром кованой бронзы и щипковой струны.

```text
Two-note extra-turn reward cue for a fantasy mining arena. Immediate short low hammered-bronze note, then a higher, fuller bronze-and-plucked-string major accent at 0.12 seconds. Playful upbeat ta-DAAA shape, crisp attacks and short warm decay. Instrumental, no vocals, gong, glitter, drum fill or ambient bed.
```

### Время хода истекло · `m3_timer_end.wav`

**0,24–0,40 с.** Один короткий сигнал окончания. Турнирный отсчёт продолжает использовать свой тик.

```text
A mechanical turn timer in a mining arena reaches its stop. One definite spring release and a muted iron dead-stop clack with a small falling pitch. Immediate, dry and easy to identify. No ticking, repeated beeps, alarm, gong or voice.
```

### Начало боя · `start_battle_2.wav`

**1,5–2,0 с**, первый прогон **1,8 с**. Характерный тембр — низкое тело гонга и суховатый верхний обертон в одном ударе.

```text
Signature fight-start cue for an underground arena. One immediate mallet strike on a thick, hand-hammered bronze gong. Warm low-mid bloom and a distinctive thin bronze overtone rising out of the same hit, then a tight stone-room decay. Whole cue about 1.8 seconds. No second hit, drums, voice, giant sub-bass or trailing wash.
```

### Победа · `m3_victory.wav`

**0,95–1,30 с.** Короткое восходящее разрешение. В текущей игре этот же файл звучит при ничьей, поэтому акцент сдержанный.

```text
Short resolved fight-result sting for a dark mining expedition. A quiet hammered-bronze note opens into two close rising tones, warmly resolving with a tiny forge resonance. Understated earned relief, quick finish. No crowd, voice, full gong strike, orchestra or triumphant fanfare.
```

### Поражение · `m3_defeat.wav`

**0,75–1,10 с.** Короткое завершение боя с нисходящим рисунком.

```text
Short defeat cue in an underground expedition. Two low muted metal tones descend gently, like a bronze plate damped by a gloved hand. Quiet weight, small room, no harsh buzzer. End with a clean dark decay. No voice, explosion, drums or long score.
```

## Меню, арена, мастерская и шахта

Имена файлов соответствуют текущим клипам в `Assets/_Project/Audio/SFX/Expedition/`.

### Нажатие и выбор · `ui_click.wav`

**0,07–0,14 с.** Частый сигнал, в том числе при выборе клетки для способности.

```text
Single tiny close-up click of a glove pressing a brass-capped spring button in a miner's field kit. Soft tight attack, slight dry metal detail, almost no tail. Very light, warm and repeatable. No digital beep, bell, paper rustle or room wash.
```

### Назад, закрыть, отменить · `ui_back.wav`

**0,12–0,20 с.** Ответный звук того же механизма с лёгким снижением высоты.

```text
A small brass spring latch releases and seats back into leather, one soft downward click. Same close tactile tone as a glove-operated expedition kit button, a little lower and softer. Short and dry. No digital beep, ring, music or voice.
```

### Недоступное действие · `ui_blocked.wav`

**0,16–0,27 с.** Ясный, спокойный отказ, который выдерживает повторные попытки.

```text
One gloved attempt to turn a locked iron catch: a short muted two-part rattle and a dead stop. Small, dry, restrained low-mid sound, distinct from a successful button click. No alarm, long scrape, threatening bass or voice.
```

### Вход в очередь арены · `queue_enter.wav`

**0,30–0,50 с.** Подтверждение записи в очередь, без боевого гонга.

```text
A small stamped brass registration token drops into a padded tray at a mining arena desk. A dry slot click followed by one quiet settling tap. Positive and expectant, close-miked and short. No arena gong, music, crowd or voice.
```

### Запуск или ускорение крафта · `craft_start.wav`

**0,35–0,60 с.** Начало работы в кузнице, один хорошо различимый акцент.

```text
One short workshop confirmation: a heavy forge lever seats, then a soft breath of leather bellows catches. Tactile iron, wood and air, close underground workshop. Compact attack and decay. No hammer barrage, music, crowd or large burst of flames.
```

### Предмет готов · `craft_ready.wav`

**0,45–0,70 с.** Чёткая отметка готовности с тембром кузницы.

```text
A finished piece of steel touches an anvil once and gives a short warm bright ring that is immediately damped. Tiny room reflection, quietly satisfying completion signal, clear on a phone speaker. No full gong, victory fanfare, hiss or voice.
```

### Получение предмета или награды · `reward_claim.wav`

**0,32–0,55 с.** Общий звук для получения готового предмета и наград.

```text
A small metal medallion drops into a leather reward pouch and the clasp settles with a soft click. Tactile brass with a little cloth, short warm finish. The reward feels earned and tangible. No shower of coins, sparkle arpeggio, choir or voice.
```

### Открытие шахтного барьера · `barrier_unlock.wav`

**0,55–0,85 с.** Более тяжёлый механизм, который также подходит для подтверждения действия в шахте.

```text
An iron locking bolt in an underground mine gate retracts with a short geared grind and one decisive latch clunk. A small gritty stone reflection, solid and controlled. No prolonged door travel, collapse, chain loop, gong or voice.
```

## Фоновая атмосфера для новых звуковых дорожек

Эти дорожки потребуют подключения в Unity. Для атмосферных файлов попробуй **20–30 с**, **Looping включён**. Оставляй их заметно тише событий, особенно в бою.

### Убежище и меню · `amb_shelter_loop`

```text
Seamless quiet ambience inside a sheltered underground mining refuge. Soft ventilation through stone, faint steady caged-lamp air and a very distant rock-room tone. Sparse and reassuring, with plenty of space for menu clicks. No close droplets, voices, music, obvious loop seam or sudden events.
```

### Мастерская · `amb_workshop_loop`

```text
Seamless low-level ambience of a small stone forge at rest. Soft steady ember bed, quiet draft through a short flue and a trace of worn wood in the room. Keep transients rare and distant. No foreground hammer hits, voices, melody or periodic clanks.
```

### Шахта · `amb_mine_loop`

```text
Seamless ambience deep in an old slate mine. Slow cool draft along a tunnel, distant damp-stone room tone and a few irregular very soft water ticks far away. Slightly uneasy and very sparse. No footsteps, voices, monster cries, music or foreground drips.
```

### Арена · `amb_arena_loop`

```text
Seamless understated underground dueling chamber ambience. Very low steady stone-room air, a trace of distant ventilation and a faint cloth draft. Keep the midrange clear for gear impacts and the tournament tick. No crowd, cheering, percussion, pitched drones, melody or sudden sounds.
```

Если захочется именно музыки, начни с одной инструментальной дорожки Убежища в **Eleven Music**. Для первого прогона хватит 45–60 секунд. Точку петли выбирай после прослушивания. [Как задавать инструментал и петли в Eleven Music](https://elevenlabs.io/docs/overview/capabilities/music/best-practices)

```text
Instrumental only. Quiet dark-fantasy mining refuge, 60 BPM, D minor. Sparse bowed low strings, restrained hammered-dulcimer harmonics and a warm held tone. Mostly air between phrases, no vocals, heavy drums, trailer hits or climax. Keep the same understated texture at the beginning and end for looping under a mobile game menu.
```
