# PagoPaDevKit

> ⚠️ **Disclaimer**: **Progetto open source indipendente, non affiliato a PagoPA S.p.A. Emulatore a
> scopo di sviluppo e formazione, fornito senza alcuna garanzia.** Non si connette al Nodo dei Pagamenti
> reale, non muove denaro e non è destinato all'uso in produzione. I marchi pagoPA appartengono ai rispettivi titolari.

**PagoPaDevKit** è un **emulatore locale** del sistema pagoPA per il modello asincrono
**GPD + Checkout**. Riproduce in modo **fedele ai contratti ufficiali** le superfici che un Ente
Creditore (EC) attraversa, così che **il codice scritto contro il devkit funzioni identico contro
l'ambiente UAT reale cambiando solo la configurazione**.

Superfici emulate (contratti = OpenAPI/WSDL ufficiali pagoPA):

| Superficie reale | Repo ufficiale | Nel devkit |
|---|---|---|
| **GPD Debt Positions v3** (REST) | `pagopa-debt-position` (openapi_external_v3) | crea/gestisci posizioni |
| **nodeForPsp** (SOAP, PSP→Nodo) | `pagopa-api` (`wsdl/nodeForPsp.wsdl`) | verify/activate/sendPaymentOutcome + WISP |
| **paForNode** (SOAP, Nodo→EC) | `pagopa-api` (`wsdl/paForNode.wsdl`) | verifica + pagamento avviso |
| **Receipts** (REST) | `pagopa-gpd-payments` | recupero ricevute (RT) |
| **FdR – Flussi di Rendicontazione** (REST) | `pagopa-fdr` (psp + org) | rendicontazione, stato REPORTED |
| **FlussoRiversamento 1.0.4** (XML firmato) | `pagopa-api` (`FlussoRiversamento_1_0_4.xsd`) | flusso XML con firma XML-DSig |
| **ApiConfig** (REST) | `pagopa-api-config` (`IbanController`) | registro **IBAN per ente** + **Tassonomia** (tipi dovuto) |

---

## Architettura e flusso

```
        (1) crea+pubblica posizione v3 (REST, toPublish=true)
 ┌────────────┐  POST /organizations/{ofc}/debtpositions   ┌─────────────────────────────┐
 │  DemoEnte  │ ─────────────────────────────────────────▶ │            GPD = EC         │
 │ gestionale │ ◀──────────── NAV + stato ───────────────  │  (porta 5001)               │
 │  (5003)    │                                             │  • Debt Positions v3 (REST) │
 └─────┬──────┘  (7) PULL stato/ricevuta (REST) ◀────────▶ │  • paForNode (SOAP)         │
       │ "Paga ora"                                         │  • Receipts (REST)          │
       ▼                                                    │  • FdR + FlussoRiversamento │
 ┌──────────┐   (2) /pagamento   ┌────────────┐            └─────┬───────────────────────┘
 │Cittadino │ ─────────────────▶ │  Checkout  │   (3) nodeForPsp     ▲  (4) paForNode
 │(browser) │   (paga carta)     │  = PSP/WISP│   verify/activate/   │  paVerify/paGetPaymentV2/
 └──────────┘                    │   (5002)   │   sendPaymentOutcome │  paSendRTV2 → PAID + RT
                                 └─────┬──────┘ ───────▶ ┌────────┐ ─┘
                                       │                 │  Nodo  │
                                       └────────────────▶│ (5004) │
                                       (5) nodoInvia      └────────┘
                                       FlussoRendicontazione (XML firmato)
                                                              │ (6) ingest → GPD: rate REPORTED
                                                              ▼
```

1. L'ente crea e pubblica la posizione su **GPD v3**.
2. Il cittadino apre il **Checkout** (qui fa da **PSP/WISP**).
3. Il Checkout chiama il **Nodo** via SOAP **`nodeForPsp`** (`verifyPaymentNotice` →
   `activatePaymentNotice` → `sendPaymentOutcomeV2`).
4. Il **Nodo** parla con l'EC (GPD) via SOAP **`paForNode`** (`paVerifyPaymentNotice` →
   `paGetPaymentV2` → `paSendRTV2`) → posizione **PAID** + **RT**.
5. Il PSP costruisce e **firma** (XML-DSig) il **FlussoRiversamento 1.0.4** e lo invia al Nodo
   (`nodoInviaFlussoRendicontazione`).
6. Il Nodo lo inoltra a GPD che **verifica la firma**, lo acquisisce e porta le rate a **REPORTED**.
7. L'ente **NON riceve push**: rilegge stato/ricevuta da GPD in **PULL** (come nel reale).

---

## Struttura della solution

| Progetto | Ruolo | Porta |
|---|---|---|
| `PagoPaDevKit.Mock.Gpd` | EC: GPD v3 + paForNode SOAP + Receipts + FdR + FlussoRiversamento | 5001 |
| `PagoPaDevKit.Mock.Checkout` | Checkout/**PSP** (ASP.NET Core MVC), chiama il Nodo via nodeForPsp | 5002 |
| `PagoPaDevKit.Mock.Nodo` | **Nodo**: nodeForPsp + WISP, chiama l'EC via paForNode | 5004 |
| `PagoPaDevKit.Mock.ApiConfig` | **ApiConfig**: registro IBAN per ente + Tassonomia | 5005 |
| `PagoPaDevKit.Client` | Libreria client riutilizzabile (typed HttpClient, v3) | — |
| `PagoPaDevKit.DemoEnte` | Gestionale Ente Creditore (ASP.NET Core MVC) | 5003 |
| `PagoPaDevKit.Tests` | Test di integrazione end-to-end (xUnit) | — |

Vincoli: **.NET 9**. Le API (GPD, Nodo) sono **Minimal API + SOAP** (SoapCore); i frontend web
(Checkout, DemoEnte) sono **ASP.NET Core MVC** (Controller + Razor views). **EF Core + SQLite**
con `DbContext` usato direttamente in endpoint/controller, **nessun** Repository / UnitOfWork /
Facade / AutoMapper.

---

## Avvio

### Prerequisiti
- **.NET 9 SDK** (per `dotnet run` / `dotnet test`)
- **Docker** + Docker Compose (per l'avvio orchestrato, opzionale)

### docker-compose
```bash
docker compose up --build
```
GPD <http://localhost:5001> (Swagger su `/swagger`, WSDL su `/gpd/nodo/paForNode.svc?wsdl`),
Checkout <http://localhost:5002>, gestionale Ente <http://localhost:5003>.

### dotnet run (tre terminali)
```bash
dotnet run --project PagoPaDevKit.Mock.Gpd        # 5001  (EC)
dotnet run --project PagoPaDevKit.Mock.ApiConfig  # 5005  (IBAN + tassonomia)
dotnet run --project PagoPaDevKit.Mock.Nodo       # 5004  (Nodo)
dotnet run --project PagoPaDevKit.Mock.Checkout   # 5002  (PSP)
dotnet run --project PagoPaDevKit.DemoEnte        # 5003  (Ente)
```

### Giro completo
0. **Configura un IBAN** dell'ente in **Infrastruttura → Strumenti** (<http://localhost:5003/infrastruttura/strumenti>),
   oppure premi **Seed 10 posizioni demo** che registra l'IBAN e popola subito il gestionale.
1. **Test Ente → Crea posizione**: dal builder scegli **Tipo dovuto** (tassonomia) e **IBAN** → l'ente
   crea e pubblica la posizione su GPD e mostra il **NAV** (con bottone copia e anteprima avviso).
2. **Paga ora come cittadino** (stepper in-console) oppure **Apri nel gateway Checkout**.
3. Paga con `4242 4242 4242 4242` → la posizione passa a **PAID**, viene generata la RT, arriva la
   notifica webhook (visibile in **Test Ente → Log webhook**).
4. La posizione risulta **PAGATA** (letta in pull); **ricevuta** disponibile.
5. **Test Ente → Rendicontazione (FdR)** → *«Simula pubblicazione FdR dal PSP»* → l'ente recupera il
   flusso, scarica il **FlussoRiversamento firmato** e vede la **quadratura** (rate → REPORTED).

> In alternativa apri **Infrastruttura → Scenari di test** ed esegui **Esegui tutti**: ogni scenario
> end-to-end chiama le API HTTP reali dei mock e mostra check verde/rosso, durata e request/response.

### Test
```bash
dotnet test
```

---

## Console PagoPaDevKit (DemoEnte, :5003)

Il gestionale Ente è una **console MVC** conforme alle **Linee guida di design della PA italiana
(Bootstrap Italia / Designers Italia)**: header e footer istituzionali nativi (`it-header`/`it-footer`),
font Titillium Web, icone dello sprite Bootstrap Italia, sidebar collassabile + badge ambiente
MOCK/UAT e menu **Componenti** verso gli altri servizi (Checkout, GPD Swagger, ApiConfig, Nodo WSDL).
Raccoglie **tutte** le funzionalità del devkit — nessuna è raggiungibile solo per URL diretto;
l'inventario completo è in [`INVENTARIO.md`](INVENTARIO.md).

| Sezione | Funzioni |
|---|---|
| **Dashboard** | card riepilogo (totali / da pagare / pagati / scaduti), ultime posizioni, ultime ricevute, health dei servizi |
| **Test Ente** | **Crea posizione** (builder completo: opzioni alternative, rate, split multi-transfer, marca da bollo, `switchToExpired`; tassonomia da elenco ufficiale, IBAN per ente) · Posizioni debitorie (filtri, dettaglio strutturato + JSON + timeline, pubblica/elimina) · Ricevute · Log webhook · **Stampa avviso con QR** (layout print) · **Rendicontazione (FdR)** |
| **Test Cittadino** | **Paga un avviso** (stepper verify→activate→notify→outcome via Nodo) · Opzioni di pagamento · Pagamento spontaneo · Carte di test |
| **Infrastruttura** | **Stato servizi** (health live, refresh 10s) · **Scenari di test** (end-to-end su API reali) · **Strumenti** (generatore IUV/NAV, registro IBAN, seed 10 posizioni, **reset locale** o **reset completo** anche GPD) |
| **Documentazione** | mappa delle funzioni e dei servizi |

### Rendicontazione e riconciliazione (FdR)
La pagina **Rendicontazione (FdR)** usa i **dati reali** delle API FdR (`pagopa-fdr`): l'ente
recupera i **Flussi di Rendicontazione** pubblicati, ne vede i pagamenti, scarica il
**FlussoRiversamento 1.0.4 firmato (XML-DSig)**. Un'azione *«Simula pubblicazione FdR dal PSP»*
popola i flussi dalle ricevute (il gestionale fa da PSP: `create→add→publish`, idempotente per IUV,
con passaggio delle rate a **REPORTED**). La **quadratura per giornata** collega la catena
**RT incassate ↔ FdR rendicontati ↔ riversamento banca** (quest'ultimo è l'unico dato *mockato*,
chiaramente etichettato, perché l'accredito sul conto tesoreria non è emulabile).

### Scenari di test (Infrastruttura → Scenari)
Eseguibili uno per uno o tutti insieme; nessun risultato è simulato. Scenari inclusi:
**Ciclo completo OK**, **Carta KO**, **Carta scaduta**, **Subscription key errata** (401 atteso),
**IUPD duplicato** (409 atteso), **Avviso scaduto**, **QR avviso coerente** (NAV nel QR == NAV emesso).

---

## Carte di test

| PAN | Esito |
|---|---|
| `4242 4242 4242 4242` | ✅ Pagamento OK |
| `4000 0000 0000 0002` | ❌ Autorizzazione negata |
| `4000 0000 0000 0069` | ❌ Carta scaduta |

Con esito KO **non** viene inviata la `paSendRTV2`: la posizione resta pagabile.

---

## Endpoint per superficie

### GPD Debt Positions v3 (REST, porta 5001)
```
POST   /organizations/{ofc}/debtpositions[?toPublish=true]      createPosition
GET    /organizations/{ofc}/debtpositions[?page&limit&due_date_from&due_date_to&status&orderby&ordering]
GET    /organizations/{ofc}/debtpositions/{iupd}
PUT    /organizations/{ofc}/debtpositions/{iupd}
DELETE /organizations/{ofc}/debtpositions/{iupd}
POST   /organizations/{ofc}/debtpositions/{iupd}/publish
```
Modello v3: `PaymentPositionModelV3` → `paymentOption[]` (con **debitore**) → `installments[]`
(rata: `iuv`/`nav`/`amount`) → `transfer[]` (`iban` **XOR** `stamp`). Stati posizione:
`DRAFT, PUBLISHED, VALID, UNPAYABLE, PARTIALLY_PAID, PAID`; stati rata: `UNPAID, PAID,
PARTIALLY_REPORTED, REPORTED, UNPAYABLE, EXPIRED`. **NAV** = aux digit `3` + **IUV** (17 cifre).

### Checkout Carts API (REST, porta 5002)
```
POST /carts   crea il carrello (paymentNotices max 5 + 4 returnUrls) -> 302 verso /pagamento?cartId=
```

### nodeForPsp (SOAP, Nodo porta 5004, `/nodo/node/nodeForPsp.svc`)
```
verifyPaymentNotice              verifica avviso (PSP -> Nodo)            [DA AVVISO]
demandPaymentNotice              genera avviso on-demand per un servizio  [SPONTANEO]
activatePaymentNotice            attivazione + emissione paymentToken (1 per avviso)
pspNotifyPaymentV2               notifica pagamento PSP -> Nodo (step 7-8)
sendPaymentOutcomeV2             esito UNICO per tutti i token del carrello -> RT all'EC
nodoInviaFlussoRendicontazione   invio FlussoRiversamento firmato (base64)
```
Il Nodo include uno **shim WISP** (catalogo/selezione PSP). Esito `ctResponse` OK/KO.

### paForNode (SOAP, GPD porta 5001, `/gpd/nodo/paForNode.svc`)
```
paVerifyPaymentNotice   verifica avviso (Nodo -> EC)
paGetPaymentV2          attivazione/lettura dati pagamento
paSendRTV2              invio ricevuta -> posizione PAID + RT
paDemandPaymentNotice   genera avviso on-demand (pagamento spontaneo) -> posizione Origin=SPONTANEO
```
Esito `ctResponse` OK/KO. Fault con **faultCode/faultString/description** identici a pagoPA
(catalogo PaaErrorEnum): `PAA_PAGAMENTO_SCONOSCIUTO` ("pagamento sconosciuto" / "L'id del
pagamento ricevuto non esiste"), `PAA_PAGAMENTO_DUPLICATO`, `PAA_PAGAMENTO_SCADUTO`, …

**Due tipi di pagamento (come pagoPA reale):**
- **Da avviso**: l'ente ha già emesso l'avviso (NAV) → `verifyPaymentNotice`/`paVerifyPaymentNotice`.
- **Spontaneo**: nessun avviso → il cittadino sceglie un servizio e l'avviso è generato on-demand
  → `demandPaymentNotice`/`paDemandPaymentNotice`. La posizione nasce con `origin=SPONTANEO`
  (le altre con `origin=AVVISO`). Sul Checkout: pagina iniziale con le due modalità.

**Caso d'uso "pagamento presso frontend dell'EC"** (SANP). Ingresso fedele via **Carts API**:
l'EC (DemoEnte backend, `/paga`) fa **`POST /carts`** sul Checkout con `paymentNotices[]`
(**max 5**: noticeNumber, fiscalCode, amount, companyName, description, allCCP), le **4 `returnUrls`**
(`returnOkUrl`/`returnCancelUrl`/`returnErrorUrl`/`returnWaitingUrl`), `emailNotice`, `idCart`;
il Checkout risponde **302** verso la pagina di pagamento. Poi: `activatePaymentNotice` per ogni
avviso → paymentToken; **`pspNotifyPaymentV2`** (paymentList); **un solo** `sendPaymentOutcomeV2`
per tutti i token → il Nodo emette una `paSendRTV2` per avviso → posizioni PAID → il Checkout
**redirige a `returnOkUrl`/`returnErrorUrl`** con l'esito.

Errori: il faultBean del Nodo segue la guida **Gestione degli errori** — quando rigira un errore
dell'EC usa `faultCode=PPT_ERRORE_EMESSO_DA_PAA` + `originalFaultCode/String/Description` con il
fault `PAA_*` originale dell'ente.

### Receipts (REST, porta 5001)
```
GET /payments/{ofc}/receipts[?pageNum&pageSize&debtor&from&to&debtorOrIuv]   lista (PaymentsResult)
GET /payments/{ofc}/receipts/{iuv}                                            RT in application/xml
```

### FdR – Flussi di Rendicontazione (REST, porta 5001)
```
# Lato PSP (pubblicazione)
POST   /psps/{pspId}/fdrs/{fdr}                 crea flusso
PUT    /psps/{pspId}/fdrs/{fdr}/payments/add    aggiungi pagamenti
PUT    /psps/{pspId}/fdrs/{fdr}/payments/del    rimuovi pagamenti
POST   /psps/{pspId}/fdrs/{fdr}/publish         pubblica -> rate REPORTED
DELETE /psps/{pspId}/fdrs/{fdr}                 elimina (solo CREATED)
GET    /psps/{pspId}/created | /published
# Lato Organization (recupero)
GET    /organizations/{org}/fdrs[?page&size&pspId&publishedGt]
GET    /organizations/{org}/fdrs/{fdr}/revisions/{rev}/psps/{pspId}
GET    /organizations/{org}/fdrs/{fdr}/revisions/{rev}/psps/{pspId}/payments
# FlussoRiversamento 1.0.4 (XML firmato XML-DSig)
GET    /organizations/{org}/fdrs/{fdr}/revisions/{rev}/psps/{pspId}/flussoriversamento   recupero XML firmato
POST   /fdr/nodo/flussoriversamento                                                       ingest (verifica firma) [Node-facing]
```

### ApiConfig — IBAN + Tassonomia (REST, porta 5005)
```
GET    /creditorinstitutions/{ci}/ibans            elenco IBAN dell'ente (ibans_enhanced)
GET    /creditorinstitutions/{ci}/ibans/list       elenco paginato
POST   /creditorinstitutions/{ci}/ibans            crea IBAN (IbanEnhanced, snake_case)
PUT    /creditorinstitutions/{ci}/ibans/{iban}     aggiorna IBAN
DELETE /creditorinstitutions/{ci}/ibans/{iban}     elimina IBAN
GET    /taxonomies[?onlyValid=true]                Tassonomia (tipi dovuto)
```
**Tipo dovuto = Tassonomia.** Il `transfer.category` è il codice tassonomico
`<prefisso>/<tipoEnte 2><macroArea 2><tipoServizio 3><motivo 2 lett>/` (es. `9/0101002IM/`).
Nel form DemoEnte scegli il **Tipo dovuto** (→ `category`) e l'**IBAN** (→ `transfer.iban`) dal
registro dell'ente. Gli IBAN si configurano in **Infrastruttura → Strumenti**
(<http://localhost:5003/infrastruttura/strumenti>, → ApiConfig). **Ogni ente ha il proprio registro
IBAN**: nello split multi-transfer l'IBAN dipende dall'ente beneficiario (`organizationFiscalCode`).

Autenticazione GPD/Receipts/FdR/ApiConfig: header **`Ocp-Apim-Subscription-Key`** (401 stile APIM
se mancante/errata). Le primitive **SOAP** sono Nodo-facing e non usano la subscription key APIM.

**Errori fedeli a pagoPA.** Il backend genera gli **stessi codici e diciture** del reale: GPD
risponde `ProblemJson {title, status, detail}` con i messaggi del catalogo `AppError` di
`pagopa-debt-position` (es. duplicato → *"The debt position violated constraints of uniqueness"*);
ApiConfig usa il catalogo IBAN (*"IBAN already exist"*, …); le primitive SOAP usano i fault
`PAA_*` con faultString/description ufficiali. **I client sono volutamente "stupidi"**: non
validano, lasciano sbagliare, e l'errore arriva dal backend come in produzione.

---

## Libreria client

`PagoPaGpdClient` (typed HttpClient, modello v3): `CreateDebtPositionAsync`, `PublishAsync`,
`GetDebtPositionAsync`, `DeleteAsync`, `GetReceiptByIuvAsync`. Configurazione via **options pattern**
(`BaseUrl`, `SubscriptionKey`). Helper `GeneratoreIuv` (IUV 17 cifre + NAV, segregation code
configurabile). **Lo stesso codice punta a UAT** cambiando solo configurazione.

---

## Dal mock all'ambiente UAT reale

**Cosa cambia (solo configurazione):**

| Impostazione | Mock | UAT |
|---|---|---|
| `PagoPaGpd:BaseUrl` | `http://localhost:5001` | `https://api.uat.platform.pagopa.it/gpd/api/v1` (path v3 secondo adesione) |
| `PagoPaGpd:SubscriptionKey` | `dev-gpd-key` | chiave reale dal **BackOffice pagoPA** |
| endpoint SOAP paForNode | `…:5001/gpd/nodo/paForNode.svc` | endpoint del Nodo/EC reale |

Passi **organizzativi** per UAT (non di codice): **adesione** sul Portale delle Adesioni,
attivazione GPD e **subscription key** dal BackOffice, configurazione segregation code/IBAN/tassonomia.

**Cosa NON cambia:** il **codice client** (`PagoPaGpdClient`, DTO v3, `GeneratoreIuv`).

**Cosa è ora fedele (gap chiusi):**
- catena completa **PSP → Nodo (`nodeForPsp`) → EC (`paForNode`)**, non più scorciatoie;
- **FlussoRiversamento 1.0.4** in XML reale con **firma XML-DSig** verificata in ingest.

**Limiti residui del mock:**
- niente PSP reali / circuito carte: il Checkout è una pagina fittizia con carte di test;
- lo **WISP** è uno *shim* (catalogo/selezione PSP): il WISP storico è dismesso in pagoPA reale;
- non sono emulati il carrello RPT legacy né il **circuito bancario** di incasso/riversamento
  (l'accredito sul conto tesoreria è **MOCKATO** nella console, chiaramente etichettato);
- la **rendicontazione** del PSP non è automatica: la si avvia da console (*«Simula pubblicazione
  FdR dal PSP»*); il meccanismo (`create→add→publish`, REPORTED, FlussoRiversamento firmato) è reale;
- la firma del FlussoRiversamento usa un **certificato dev self-signed** (in produzione è il
  certificato del PSP, con catena di fiducia); nessuna marcatura temporale;
- validazioni semplificate (es. algoritmo di check IUV).

> Implementati anche **`nodeForPa`** (`nodoChiediElencoFlussiRendicontazione` /
> `nodoChiediFlussoRendicontazione`) ed **E-bollo 2.0** (marca da bollo digitale). Esiste un endpoint
> **dev-only** `POST /dev/reset/{ofc}` (fuori dal contratto pagoPA) usato dal *reset completo* della
> console per azzerare i dati di un ente su GPD, lasciando intatti IBAN e tassonomie su ApiConfig.

---

## Licenza

Il codice di questo progetto è distribuito sotto **Apache License 2.0** — vedi il file
[`LICENSE`](LICENSE). Copyright © 2026 Filippo D'Errigo. Il file [`NOTICE`](NOTICE) riporta
l'attribuzione e la natura del progetto.

PagoPaDevKit incorpora componenti di terze parti (Bootstrap Italia, Bootstrap, jQuery, font,
pacchetti NuGet), ciascuno con la propria licenza: l'elenco completo è in
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md). La licenza Apache-2.0 del progetto **non** si
applica a tali componenti.

### Avvertenza sui marchi
**Progetto open source indipendente, non affiliato a PagoPA S.p.A. Emulatore a scopo di sviluppo e
formazione, fornito senza alcuna garanzia.** «pagoPA» e i marchi correlati appartengono ai
rispettivi titolari e sono citati al solo scopo di descrivere i contratti tecnici pubblici che
l'emulatore riproduce. Il software non si connette al Nodo dei Pagamenti reale e non movimenta denaro.
