# Note personali — build di Desktop Frames +

Questo file esiste **solo sul ramo `personal-build`** e non va mai proposto a
limbo666. È scritto in italiano di proposito: serve a me, non al progetto. Tutto
il resto — codice, commenti, messaggi di commit — resta in inglese, perché quello
sì finisce nelle proposte.

---

## Cosa installare su un PC nuovo

**1. Visual Studio Build Tools 2022** (gratuito, non serve Visual Studio intero)

Scarica da https://visualstudio.microsoft.com/downloads/ → *Tools for Visual
Studio* → **Build Tools for Visual Studio 2022**. In fase di installazione
seleziona il carico di lavoro **.NET desktop build tools**.

MSBuild finisce qui — **la variante amd64, non quella accanto**:

```
C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe
```

Quella senza `amd64` è a 32 bit e cerca un `dotnet` a 32 bit. Con l'SDK x64 —
cioè quello normale — fallisce con `MSB4236: l'SDK 'Microsoft.NET.Sdk' non è
stato trovato`, che sembra un SDK mancante e non lo è. Su questa macchina la
prima volta ha funzionato lo stesso solo perché `dotnet` era già nel PATH della
shell; su un PC pulito no.

**2. .NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0

**3. Git** e, se comodo, GitHub Desktop.

Entrambi si installano da riga di comando, il che evita di cercare la spunta
giusta dentro l'installer di Visual Studio:

```bash
winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements
```

```bash
winget install --id Microsoft.VisualStudio.2022.BuildTools -e --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --includeRecommended"
```

### Perché non basta `dotnet build`

Il progetto usa un riferimento COM (`IWshRuntimeLibrary`, quello che legge e
scrive i collegamenti `.lnk`) con `WrapperTool` impostato a `tlbimp`. MSBuild di
.NET Core non sa gestirlo e si ferma con
`ResolveComReference ... not supported`. Serve MSBuild "classico", quello dei
Build Tools. Non è un difetto da correggere: è come il progetto è fatto.

---

## Compilare

```bash
export PATH="/c/Program Files/dotnet:$PATH"
cd "Code"
"/c/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/amd64/MSBuild.exe" "Desktop Frames.sln" -p:Configuration=Release -v:minimal -nologo
```

La riga `export` serve nelle shell aperte prima di installare l'SDK, che non ne
conoscono ancora il percorso. Metterla sempre non costa niente.

Il risultato finisce in
`Code/Desktop Frames/bin/Release/net8.0-windows7.0/`, con una sottocartella per
ogni lingua (`it`, `fr`, `es`, `de`, `pt`, `ru`, `pl`, `zh-Hans`) che contiene il
pacchetto di traduzione.

### Se si lamenta di `project.assets.json`

Succede dopo un cambio di ramo che tocca `obj/`. Ripristina i pacchetti e
ricompila:

```bash
"…/MSBuild.exe" "Desktop Frames.sln" -t:restore -v:quiet -nologo
```

---

## Installare la build compilata

> **Si compila sempre da `personal-build`.** Mai da un ramo di lavoro.
>
> Sono due decisioni diverse, ed è facile confonderle in una:
>
> | | Base | Perché |
> |---|---|---|
> | dove si scrive una novità | `upstream/main` | perché la PR parta da una base comune con limbo666 |
> | cosa si installa | `personal-build` | perché è l'unica che ha tutto |
>
> Un ramo di proposta parte da `upstream/main` e quindi **non contiene** portal,
> wildcard, avvio via task, correzioni delle icone. Installarlo fa sparire tutto
> quello in silenzio: l'app funziona, l'avvio automatico pure — perché il task in
> Windows resta — e ci si accorge del vuoto solo settimane dopo, davanti a una
> funzione che non c'è più.
>
> È successo il 7 settembre 2026, per giorni, con il ramo dell'agenda.

L'app installata sta in:

```
%LOCALAPPDATA%\Programs\DesktopFramesPlus-<versione>
```

Si aggiorna copiandoci sopra il contenuto della cartella `bin/Release/…`, ad app
chiusa. **Non toccare la sottocartella `Profiles`**: contiene i dati veri.

```bash
# chiudere l'app, poi (adattare la versione nel percorso):
cp -r "Code/Desktop Frames/bin/Release/net8.0-windows7.0"/* \
      "$LOCALAPPDATA/Programs/DesktopFramesPlus-2.7.8"/
```

---

## Dove guardare quando qualcosa va storto

| Cosa | Dove |
|---|---|
| Log dell'app | `DesktopFramesPlus-2.7.8\Desktop_Frames.log` |
| Frame, posizioni, colori | `Profiles\Default\frames.json` |
| Impostazioni | `Profiles\Default\options.json` |
| Regole di auto-organizzazione | `Profiles\Default\auto_organize.json` |
| Copia di sicurezza dei frame | `Profiles\Default\frames.json.bak` |

Il log si attiva dalle opzioni. Vale la pena tenerlo acceso: più di una volta ha
distinto un guasto vero da un problema di sola visualizzazione, e senza avremmo
corretto la cosa sbagliata.

I crash di .NET non finiscono nel log dell'app ma nel registro eventi di Windows:

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='.NET Runtime'} |
  Select-Object -First 3 -ExpandProperty Message
```

---

## Smart App Control: il nemico da riconoscere

Costato quattro giorni, il 26-30 agosto 2026. Vale la pena saperlo riconoscere in
cinque minuti la prossima volta, su questo PC o su un altro.

**Cos'è.** Una protezione di Windows 11 che giudica ogni eseguibile e ogni DLL
per firma e reputazione. Quello che compili tu non è firmato e non ha
reputazione, quindi ogni build è un tiro di dado. Non ha esclusioni, non legge il
tuo archivio certificati, e non esiste modo di dirgli che un file è tuo. Un
certificato autofirmato non serve a niente; uno vero deve prima costruirsi una
reputazione che un fork personale non avrà mai.

**Come si presenta.** Tre facce diverse dello stesso problema, e nessuna dice il
proprio nome:

| Cosa vedi | Cosa è successo |
|---|---|
| interfaccia in inglese su Windows italiano | bloccato `it\Desktop Frames.resources.dll`, e .NET ricade in silenzio sulle risorse neutre |
| l'app non parte, nessun messaggio | bloccato `Desktop Frames.dll` |
| non parte con Windows, ma a mano sì | bloccato l'exe alla creazione del processo |

**La trappola che mi ha fatto perdere due giorni.** Un blocco al *caricamento di
una DLL* lascia l'evento CodeIntegrity 3077. Un blocco alla *creazione del
processo* **non lascia niente**: né 3077, né l'evento 9707 di Explorer, perché il
comando non è mai partito. Cercare 3077 e non trovarlo non assolve SAC.

**Come si verifica in due comandi.**

```powershell
(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState
```

0 = spento, 1 = acceso, 2 = valutazione.

```powershell
Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-CodeIntegrity/Operational';Id=3077,3033} |
  Select-Object -First 5 TimeCreated, Message
```

E per sapere se Explorer ha davvero provato ad avviare qualcosa all'accesso —
l'evento 9707 elenca ogni comando lanciato dalla chiave `Run`:

```powershell
Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Shell-Core/Operational';Id=9707} |
  Sort-Object TimeCreated | Select-Object TimeCreated, Message
```

**Il rimedio provvisorio, se serve far girare l'app subito senza toccare SAC:**
i pacchetti `.resx` nella cartella `Languages` accanto all'eseguibile sono file
di testo, non codice, e SAC non ha niente da bloccare. Coprono la traduzione, non
l'eseguibile. Da togliere appena i satelliti compilati tornano a caricarsi: i
pacchetti **vincono** sulle risorse compilate, e dimenticarli lì significa
ritrovarsi stringhe vecchie dopo un aggiornamento, senza capire perché.

**Il rimedio vero.** Spegnerlo, dal pannello Sicurezza di Windows → Controllo app
e browser. Windows avverte che è irreversibile; sul mio sistema l'opzione per
riaccenderlo è rimasta lo stesso, ma non ci conterei. Se il pannello offre
**Valutazione**, quella è la scelta migliore: non blocca, osserva, e su una
macchina dove si compila dovrebbe spegnersi da sé.

---

## Icone che diventano pagine bianche

**Non è l'app: è la cache delle icone di Windows.** Mezza giornata per arrivarci,
il 1° settembre 2026. Trenta secondi la prossima volta.

Il sintomo: alcuni elementi mostrano la pagina bianca al posto dell'icona. Il
collegamento funziona, il programma parte, ma l'icona non torna **né riavviando
l'app, né riavviando il PC, né togliendo e rimettendo l'elemento**.

Succede quando la cache delle icone perde le voci di alcuni programmi — dopo un
loro aggiornamento o una riparazione. Da quel momento la shell risponde a
`SHGetFileInfo` con l'icona generica, e l'app disegna quello che le viene dato.

**Il rimedio:**

```bash
ie4uinit.exe -show
```

Se non basta, la versione completa — chiude Explorer, cancella le cache, lo
riavvia. La barra delle applicazioni sparisce per qualche secondo:

```bash
taskkill /f /im explorer.exe & del /a /q "%LOCALAPPDATA%\IconCache.db" & del /a /q "%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache*.db" & start explorer.exe
```

Poi chiudi e riapri l'app.

### Come riconoscerlo in un minuto, invece che in mezza giornata

La prova che scagiona l'app: estrai l'icona **leggendo l'eseguibile**, che non
passa dalla cache della shell. Se da qui esce un'icona piena di pixel mentre
nella frame resta bianca, il guasto è nella cache di Windows.

```powershell
Add-Type -AssemblyName System.Drawing
$bmp = [System.Drawing.Icon]::ExtractAssociatedIcon('C:\Program Files\Inkscape\bin\inkscape.exe').ToBitmap()
$n = 0; for ($y=0; $y -lt $bmp.Height; $y++) { for ($x=0; $x -lt $bmp.Width; $x++) { if ($bmp.GetPixel($x,$y).A -gt 10) { $n++ } } }
"pixel visibili: $n / $($bmp.Width * $bmp.Height)"
```

### La lezione di metodo, che vale oltre le icone

Ho perso quelle ore leggendo il codice per dedurre quale strada prendesse,
inseguendo tre ipotesi sbagliate di fila. Quando finalmente ho messo una riga di
log nel ramo sospetto, la sonda **non ha stampato niente** — e quel silenzio ha
detto in un colpo solo che stavo leggendo un file che non veniva mai eseguito.

Quando un difetto non torna, strumentare prima e leggere dopo. Una sonda che non
stampa è già una risposta.

---

## I rami e a cosa servono

| Ramo | Cos'è | Stato |
|---|---|---|
| `main` | il codice di limbo666, intatto | — |
| `add-localization` | traduzione: motore + 9 pacchetti | **fuso**, PR #136 |
| `fix-altgr-spotsearch` | hotkey ricerca che non mangia AltGr | **fuso**, PR #138 |
| `fix-extension-wildcards` | correzione dei jolly nelle estensioni | PR #139 aperta, chiude la issue #135 |
| `translate-missed-strings` | 5 testi sfuggiti alla localizzazione | PR #140 aperta |
| `wait-for-desktop` | attende il desktop di Explorer invece di darlo per scontato | pronto, da proporre |
| `portal-paste-and-drop` | trascinamento e incolla nei portal | da ribasare, poi proporre |
| `portal-folder-tracking` | frame che non si perde se la cartella cambia | da ribasare, poi proporre |
| `startup-scheduled-task` | avvio con Windows via attività al logon | **mai**: cambia il comportamento per tutti |
| `personal-build` | tutti insieme, la build che uso io | **mai** |

I rami di proposta **non condividono file fra loro**: così limbo666 può
accettarne uno e rifiutare gli altri, in qualunque ordine.

Con un'eccezione consapevole: `translate-missed-strings` traduce anche il titolo
del frame creato al primo avvio, e quella riga sta in `FrameManager.cs`, che i
due rami portal toccano. Sono regioni lontanissime dello stesso file e git le
fonde da sé. La regola serve a evitare conflitti, non a evitare che due rami
nominino lo stesso file.

### Descrizioni delle PR: corte

Il ragionamento va nel **messaggio di commit**, che resta attaccato al codice per
sempre. La descrizione della PR serve a chi decide se aprirla: bastano il difetto
in una riga, la garanzia che niente di esistente cambia comportamento, e il
`Closes #nnn` se c'è una issue. La prima versione della #139 era lunga il triplo
e non diceva niente di più.

### Le issue aperte da te sono la corsia preferenziale

La #139 nasce dalla issue #135, aperta mesi prima con l'analisi della causa
dentro. Chi rilegge non deve né riprodurre il difetto né decidere se sia un
difetto: è già scritto, da chi lo ha trovato. Vale la pena aprire la issue anche
quando la correzione ce l'hai già in mano.

`personal-build` contiene in più alcuni commit di traduzione delle funzionalità
nuove. Non possono stare sui rami di proposta: su quello della traduzione
descriverebbero funzioni inesistenti, su quelli delle funzioni manca il sistema
delle traduzioni. Esistono solo dove i due si incontrano.

### Regola da non dimenticare

Quando si aggiunge qualcosa, **pubblicare anche `personal-build`**. È l'unico
posto dove quei commit di collegamento esistono: se resta indietro, quel lavoro
vive solo su questo disco.

---

## Riallinearsi al codice nuovo di limbo666

```bash
git fetch upstream
git checkout add-localization
git rebase upstream/main
```

Poi ricostruire `personal-build` riportandoci sopra, in ordine: il commit di
`fix-extension-wildcards`, quelli di `portal-paste-and-drop`, quelli di
`portal-folder-tracking`, e infine i commit di traduzione.

**Un conflitto si ripresenta ogni volta**, in `PortalFrameManager.cs` intorno
alla voce Rinomina. Si risolve tenendo **entrambe** le righe:

```csharp
AddPasteMenuItem(contextMenu);

MenuItem renameItem = new MenuItem { Header = Strings.MenuRenameItem };
```

Dopo un riallineamento la storia è riscritta, quindi GitHub Desktop propone il
*pull* invece del *push*. **Non accettarlo**: rimetterebbe dentro la versione
precedente. La via che funziona è cancellare il ramo su github.com e poi
*Publish branch*.

### I commit già fusi spariscono, ed è giusto così

`personal-build` porta la copia locale di commit che nel frattempo limbo666 ha
accettato — oggi quello dell'AltGr. Al rebase git li riconosce come già applicati
e li lascia cadere. Non è un errore e non hai perso niente: quel codice adesso
arriva da `main`.

---

## L'app è portatile

Non serve installarla. L'app cerca i propri dati **accanto al proprio
eseguibile**:

```csharp
_appBaseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
_profilesRootDir = Path.Combine(_appBaseDir, "Profiles");
```

Quindi la cartella compilata si può copiare dove si vuole — anche su una
chiavetta — e funziona con impostazioni proprie, senza toccare niente del
computer e senza interferire con l'installazione principale. Per rimuoverla si
cancella la cartella.

L'unica cosa che *non* è portatile è l'avvio automatico: l'attività al logon
registra il percorso completo dell'eseguibile. Sul ramo `startup-scheduled-task`
il programma se ne accorge da solo — all'avvio confronta il percorso nel task con
dove si trova davvero, e se differiscono riscrive il task. Sposta pure la
cartella, l'avvio la segue al primo lancio successivo.

---

## Fare lo zip autonomo (gira senza .NET installato)

Serve per usare l'app su un computer dove non si vuole installare nulla. Sono
circa 178 MB estratti, 79 compressi, perché si porta dietro il runtime .NET.

**1. Scaricare i pacchetti runtime** — questo passaggio va fatto con `dotnet`,
non con MSBuild, altrimenti la pubblicazione si ferma con `NETSDK1112`:

```bash
cd "Code/Desktop Frames"
dotnet restore "Desktop Frames.csproj" -r win-x64
```

**2. Pubblicare:**

```bash
cd "Code"
"…/MSBuild.exe" "Desktop Frames/Desktop Frames.csproj" -t:publish \
  -p:Configuration=Release -p:SelfContained=true -p:RuntimeIdentifier=win-x64 \
  -p:PublishDir="pubauto\\" -v:minimal -nologo
```

**3. Aggiungere licenze e istruzioni** dentro `pubauto`: `License.md` e
`Code/LICENSE` (l'MIT chiede che le note di copyright viaggino con le copie) più
un `LEGGIMI.txt` che dica che non è la versione ufficiale, dove sta quella vera,
e che le segnalazioni riguardano questa build.

**4. Prima di comprimere, cancellare `pubauto\Profiles`.** Se la build è stata
avviata per collaudarla si è creata i propri `frames.json` e `options.json`:
finirebbero nell'archivio, e chi lo apre si troverebbe i frame di qualcun altro.

**5. Comprimere** e verificare. Attenzione: negli archivi creati da
`Compress-Archive` i percorsi usano la barra rovesciata (`it\Desktop
Frames.resources.dll`), quindi un controllo che cerca `it/...` dà un falso
negativo.

**6. Ricordarsi di cancellare `pubauto`** dopo, per non lasciarlo nel repository.

### Perché non pubblicarlo come release su GitHub

La licenza MIT lo permette senza riserve. Il problema non è legale ma di
rapporto: una release sul fork diventa una distribuzione parallela, e le
segnalazioni degli utenti finiscono a limbo666 per una build che non ha
compilato. Con l'aggravante del tempismo, mentre gli si stanno proponendo dei
contributi. Lo zip su Drive o su una chiavetta copre lo stesso bisogno senza
nulla di tutto questo.

---

## La regola che ha salvato l'app più di una volta

I valori scritti nei file di configurazione — `Medium`, `Details`, `Gray`, i nomi
delle categorie di auto-organizzazione — **non si traducono mai**. Si traduce
l'etichetta mostrata; il valore viaggia intatto nel `Tag` dell'elemento.

Una versione iniziale traduceva una tendina la cui selezione veniva usata come
chiave di un dizionario: sceglierne una voce faceva chiudere l'app. Con questa
regola la configurazione salvata resta leggibile in qualunque lingua, e cambiare
lingua non riscrive niente.

## Test

C'è una piccola suite in `Code/Desktop Frames.Tests`, fuori dalla soluzione
apposta: l'applicazione ha referenze COM e va compilata con MSBuild classico,
mentre i test girano con `dotnet` normale.

```
dotnet test "Code/Desktop Frames.Tests"
```

Copre le due parti che sbagliano in silenzio: la fusione fra un'attività e
l'evento-ombra che ne porta l'ora (`AgendaMerge`), e quali indirizzi il frame
web può seguire (`WebPageSite.Allows`) — dove il confronto è ancorato al punto,
così `evilgoogle.com` non passa per `google.com`.

I file sotto test sono inclusi per collegamento, non tramite referenza al
progetto. Che quella logica si compili da sola, senza WPF né client Google, è
una proprietà che vale la pena non perdere; il progetto di test è ciò che se ne
accorge se smette di essere vera.
