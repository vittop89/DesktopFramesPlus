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

MSBuild finisce qui:

```
C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe
```

**2. .NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0

**3. Git** e, se comodo, GitHub Desktop.

### Perché non basta `dotnet build`

Il progetto usa un riferimento COM (`IWshRuntimeLibrary`, quello che legge e
scrive i collegamenti `.lnk`) con `WrapperTool` impostato a `tlbimp`. MSBuild di
.NET Core non sa gestirlo e si ferma con
`ResolveComReference ... not supported`. Serve MSBuild "classico", quello dei
Build Tools. Non è un difetto da correggere: è come il progetto è fatto.

---

## Compilare

```bash
cd "Code"
"/c/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/MSBuild/Current/Bin/MSBuild.exe" "Desktop Frames.sln" -p:Configuration=Release -v:minimal -nologo
```

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

L'app installata sta in:

```
C:\Users\panta\AppData\Local\Programs\DesktopFramesPlus-2.7.8
```

Si aggiorna copiandoci sopra il contenuto della cartella `bin/Release/…`, ad app
chiusa. **Non toccare la sottocartella `Profiles`**: contiene i dati veri.

```bash
# chiudere l'app, poi:
cp -r "Code/Desktop Frames/bin/Release/net8.0-windows7.0"/* "/c/Users/panta/AppData/Local/Programs/DesktopFramesPlus-2.7.8"/
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

## I rami e a cosa servono

| Ramo | Cos'è | Si propone? |
|---|---|---|
| `main` | il codice di limbo666, intatto | — |
| `add-localization` | traduzione: motore + 9 pacchetti | sì |
| `fix-extension-wildcards` | correzione dei jolly nelle estensioni | sì |
| `portal-paste-and-drop` | trascinamento e incolla nei portal | sì |
| `portal-folder-tracking` | frame che non si perde se la cartella cambia | sì |
| `personal-build` | tutti insieme, la build che uso io | **mai** |

I tre rami di proposta partono dallo stesso punto e **non condividono file fra
loro**: così limbo666 può accettarne uno e rifiutare gli altri, in qualunque
ordine.

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
