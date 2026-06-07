# INVENTARIO — PagoPaDevKit (FASE 0 censimento)

> Censimento completo di TUTTE le funzionalità esistenti prima del refactor del frontend.
> Ogni funzione ha una **collocazione nel nuovo menu**. Nessuna funzione resta orfana.
> Le anomalie / decisioni architetturali sono segnalate in fondo.

## Topologia della solution (6 progetti)

| Progetto | Porta | Tipo | Ruolo |
|---|---|---|---|
| PagoPaDevKit.Mock.Gpd | 5001 | Minimal API + SOAP | GPD v3 (posizioni debitorie), Receipts, PaymentOptions, E-bollo, FdR, SOAP paForNode |
| PagoPaDevKit.Mock.Checkout | 5002 | **MVC** | Gateway di pagamento cittadino (PSP), Carts API, pagamento spontaneo |
| PagoPaDevKit.DemoEnte | 5003 | **MVC** | Portale Ente Creditore, stazione SOAP push RT |
| PagoPaDevKit.Mock.Nodo | 5004 | SOAP | nodeForPsp (PSP→Nodo) + nodeForPa (Ente→Nodo FdR) |
| PagoPaDevKit.Mock.ApiConfig | 5005 | Minimal API | Registro IBAN + Tassonomia servizi |
| PagoPaDevKit.Client | — | libreria | PagoPaGpdClient, ApiConfigClient, GeneratoreIuv |

---

## A. PagoPaDevKit.DemoEnte (frontend da refactorare — host del nuovo console)

### Controller / Action

| Controller | Action | Verbo | Route | Funzione | Nuova collocazione |
|---|---|---|---|---|---|
| Dovuti | Index | GET | `/` | Lista dovuti + sync stato live da GPD + form crea | Dashboard + Test Ente › Posizioni debitorie / Crea dovuto |
| Dovuti | Crea | POST | `/dovuti` | Crea+pubblica posizione su GPD (toPublish), genera IUV/IUPD, metadata | Test Ente › Crea dovuto |
| Dovuti | Esito | GET | `/esito` | Callback returnUrl da Checkout (OK/KO + nav[]) | Test Cittadino › esito pagamento |
| Dovuti | Ricevuta | GET | `/dovuti/{iuv}/ricevuta` | Mostra RT (ricevuta) da GPD Receipts | Test Ente › Ricevute |
| Avviso | Avviso | GET | `/dovuti/{iuv}/avviso` | **Stampa avviso/bollettino con QR** (PAGOPA\|002), NAV, CBILL/SIA | Test Ente › Stampa avviso (print-friendly) |
| Iban | Index | GET | `/ibans` | Lista IBAN da ApiConfig | Infrastruttura › Strumenti › Registro IBAN |
| Iban | Crea | POST | `/ibans` | Crea IBAN su ApiConfig | idem |
| Iban | Elimina | POST | `/ibans/{iban}/delete` | Elimina IBAN | idem |
| Paga | Paga | GET | `/paga?nav=` | Crea cart su Checkout (POST /carts), relay 302 → gateway | Test Cittadino › Paga un avviso (handoff) |
| Riconciliazione | Index | GET | `/riconciliazione` | **Riconciliazione contabile MOCK** (FdR/riversamento sintetico) | Test Ente › Riconciliazione (banner MOCK) |

### SOAP inbound
- `StationService : IStation` — endpoint `/stazione/paForNode.svc`, riceve `paSendRTV2` in push → aggiorna Dovuto a PAGATO (ReceiptId, PSP, fee, data). → Infrastruttura › Stato servizi (endpoint) + alimenta Log webhook.

### ViewModels / Models
HomeVm, EsitoEnteVm, RiconciliazioneVm, FlussoMock, RicevutaVm, IbanAdminVm, AvvisoVm · Dovuto (entity), EnteDbContext (SQLite), CreaDovutoForm.

### Config (appsettings)
Ente: FiscalCode 77777777777, CompanyName "Comune Demo", SegregationCode 47, CodiceSia 33333, CheckoutBaseUrl/CheckoutApiUrl :5002, ReturnUrl :5003/esito · PagoPaGpd :5001 (+ profilo `_Uat`) · ApiConfig :5005 · SubscriptionKey dev-gpd-key.

### Asset attuali
Nessun wwwroot. CSS inline in `_Layout.cshtml`. → da sostituire con Bootstrap 5 vendorizzato + site.css.

---

## B. PagoPaDevKit.Mock.Checkout (gateway cittadino — resta, polish leggero)

| Controller | Action | Verbo | Route | Funzione |
|---|---|---|---|---|
| Home | Index | GET | `/` | Landing: pagamento da avviso / spontaneo |
| Home | Spontaneo | GET/POST | `/spontaneo` | **Pagamento spontaneo** → demandPaymentNotice SOAP → /pagamento |
| Pagamento | Index | GET | `/pagamento` | Carica cart (cartId o nav[]), verify per avviso, mostra form carta |
| Pagamento | Index | POST | `/pagamento` | Classifica carta test → activate→pspNotify→sendOutcome → returnUrl/esito |
| Carts | Carts | POST | `/carts` | **Carts API** eCommerce: 1-5 avvisi + returnUrls → 302 /pagamento |
| Opzioni | Index | GET | `/opzioni?nav=&fiscalCode=` | **Opzioni di pagamento** (unica/rateale) da GPD payment-options |

Client: `NodeForPspClient` (Verify/Activate/PspNotify/SendOutcome/Demand). `TestCards.Classify` (4242…=OK, 4000…0002=negata, 4000…0069=scaduta). CartStore in-memory.
Funzioni da esporre nel console come riferimento: Carte di test (Test Cittadino), Opzioni di pagamento (Test Cittadino).

---

## C. Backend API (da NON toccare nei contratti — sorgenti di dato per il console)

### GPD 5001
- `POST /organizations/{ofc}/debtpositions` (?toPublish) — crea posizione
- `GET /organizations/{ofc}/debtpositions/{iupd}` — dettaglio
- `GET /organizations/{ofc}/debtpositions` — lista paginata (filtri)
- `PUT /organizations/{ofc}/debtpositions/{iupd}` — aggiorna
- `DELETE /organizations/{ofc}/debtpositions/{iupd}` — elimina
- `POST /organizations/{ofc}/debtpositions/{iupd}/publish` — pubblica
- `GET /payment-options/organizations/{ofc}/notices/{nav}` — opzioni pagamento
- `GET /payments/{ofc}/receipts` — lista ricevute
- `GET /payments/{ofc}/receipts/{iuv}` — RT singola (XML)
- `GET /e-bollo/organizations/{ofc}/stamps/{iuv}` — marche da bollo digitali
- FdR PSP: `POST/PUT …/psps/{id}/fdrs/...` (create/add/del/publish) · FdR PA: `GET /organizations/{ofc}/fdrs[/{fdr}]`
- SOAP paForNode `/gpd/nodo/paForNode.svc`: paVerifyPaymentNotice, paGetPaymentV2, paSendRTV2, paDemandPaymentNotice

### Nodo 5004
- nodeForPsp `/nodo/node/nodeForPsp.svc`: verifyPaymentNotice, activatePaymentNotice, pspNotifyPaymentV2, sendPaymentOutcomeV2, demandPaymentNotice
- nodeForPa `/nodo/node/nodeForPa.svc`: nodoChiediElencoFlussiRendicontazione, nodoChiediFlussoRendicontazione

### ApiConfig 5005
- `GET/POST/PUT/DELETE /creditorinstitutions/{ci}/ibans[...]` — registro IBAN
- `GET /taxonomies` — tassonomia servizi (onlyValid/q/tipoEnte/limit)

---

## Mappa funzione → menu (nessun orfano)

1. **Dashboard** — card riepilogo dovuti (totali/da pagare/pagati/scaduti), ultimi 5 dovuti, ultime 5 ricevute, health 3 servizi.
2. **Test Ente**
   - Crea dovuto (Dovuti.Crea) — con tassonomia + IBAN
   - Posizioni debitorie (Dovuti.Index + GPD list) — filtri, azioni riga
   - Dettaglio posizione (nuovo, da GPD get) — JSON accordion, timeline
   - Ricevute (Dovuti.Ricevuta + GPD receipts list)
   - Log webhook (nuovo — alimentato da StationService push RT)
   - Stampa avviso/bollettino con QR (Avviso.Avviso) — print-friendly
   - Riconciliazione contabile MOCK (Riconciliazione.Index)
3. **Test Cittadino**
   - Paga un avviso (stepper in-console; integra Paga/handoff + esito) 
   - Opzioni di pagamento (GPD payment-options)
   - Pagamento spontaneo (Checkout.Spontaneo / demand)
   - Carte di test (TestCards)
4. **Infrastruttura**
   - Stato servizi (health live GPD/Checkout/Nodo/ApiConfig/Ente)
   - Scenari di test (ScenarioRunner — chiamate HTTP reali)
   - Strumenti: generatore IUV/NAV, registro IBAN, reset dati, seed 10 dovuti demo
5. **Documentazione** — sezioni + elenco funzionalità.

---

## Anomalie / decisioni segnalate

1. **Host del console = DemoEnte.** Il prompt descrive una sidebar unica con *Test Ente* + *Test Cittadino*: è un cockpit per sviluppatori. Lo costruisco in **DemoEnte** (già integra GPD, ApiConfig, Checkout, SOAP). Checkout (5002) **resta** come gateway PSP realistico (Carts redirect + test esistenti CartPaymentTests/CartsApiTests), con polish Bootstrap leggero per coerenza visiva nell'handoff.
2. **Paga un avviso (Test Cittadino):** per UX fluida aggiungo un `NodeForPspClient` in DemoEnte e gestisco lo stepper server-side (verify→activate→notify→outcome, classificazione carte test) **dentro il console**, senza salto d'app. Il path Carts→Checkout resta disponibile come alternativa "gateway reale".
3. **Riconciliazione e dati bancari sono MOCK** (non emulabili): banner rosso esplicito già presente, mantenuto e rinforzato.
4. **Nessun contratto API mock viene toccato.** Solo frontend. Test esistenti (56) non devono rompersi.
5. Funzioni senza menu esplicito nel prompt e ricollocate: **Registro IBAN** → Infrastruttura›Strumenti; **Opzioni di pagamento** e **Pagamento spontaneo** → Test Cittadino; **Riconciliazione** → Test Ente.
