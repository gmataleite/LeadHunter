# LeadHunter

CLI em **C# / .NET 9** estruturada em **Clean Architecture** para prospecção B2B automatizada com controle rigoroso de custos de API, enriquecimento web com cota diária segura, análise estruturada com **Gemini 2.5 Flash** e exportação deduplicada para o **Google Sheets**.

---

## Arquitetura da Solution

```text
LeadHunter.sln
├── campaigns/
│   └── dentistas-abc.json          # Exemplo de campanha configurável
└── src/
    ├── LeadHunter.Core/            # Entidades, CampaignConfig, PhoneNormalizer, LeadClassifier, WebDataExtractor, ProspectingService
    ├── LeadHunter.Infrastructure/  # EF Core SQLite (Migrations automáticas), GooglePlacesClient, GoogleSearchEnricher, GeminiLeadAnalyzer, GoogleSheetsExporter
    └── LeadHunter.Cli/             # CLI System.CommandLine + Spectre.Console + dotenv.net
```

### Garantias do Pipeline
- **Idempotência total (`leadhunter.db`):** nunca paga duas vezes pelo mesmo `PlaceId` e nunca exporta leads duplicados por `PlaceId` ou `TelefonePrincipal` (mesmo entre campanhas sobrepostas).
- **Circuit Breaker de Orçamento:** antes de cada página da Places API, valida `TotalSpentUsd + 0.040m <= MaxBudgetUsd`.
- **Cota Diária de Busca (95/dia):** entidade `DailyQuotaUsage` impede estourar a cota gratuita de 100 req/dia do Google Custom Search; leads excedentes recebem status `PendingQuota` e podem ser retomados no dia seguinte com `resume`.
- **Priorização em Tiers:**
  - **Tier 1 (Alta Prioridade):** sem site e sem celular no Maps.
  - **Tier 2 (Média Prioridade):** sem site com celular no Maps, ou `WebsiteUri` apontando apenas para `instagram.com`, `wa.me`, `whatsapp.com` ou `linktr.ee`.
  - **Tier 3 (Baixa Prioridade / Descarte):** site próprio estruturado (descartado se `AllowHasWebsite == false`).

---

## Passo a Passo: Habilitando as APIs no Google Cloud Console

### 1. Criar / selecionar um projeto no Google Cloud
1. Acesse [console.cloud.google.com](https://console.cloud.google.com/).
2. Crie um projeto (ex: `LeadHunter`) e vincule uma conta de faturamento (*Billing*) para habilitar a Places API.

### 2. Habilitar as 4 APIs do Google Cloud
No menu **APIs e serviços → Biblioteca**, pesquise e clique em **Ativar** em cada uma:
1. **Places API (New)** (`places.googleapis.com`)
2. **Custom Search API** (`customsearch.googleapis.com`)
3. **Google Sheets API** (`sheets.googleapis.com`)
4. **Google Drive API** (`drive.googleapis.com`)

### 3. Criar chaves de API (Places e Custom Search)
1. Vá em **APIs e serviços → Credenciais → + Criar credenciais → Chave de API**.
2. Em **Restrições do aplicativo**, selecione **Nenhum** ou **Endereços IP** (**não** use *Sites / HTTP Referrer*, pois chamadas CLI/backend não enviam cabeçalho `Referer`).
3. Em **Restrições de API**, restrinja à **Places API (New)** (e **Custom Search API**, ou crie uma chave separada para cada).

### 4. Criar o Programmable Search Engine (`GOOGLE_SEARCH_ENGINE_ID`)
1. Acesse [programmablesearchengine.google.com](https://programmablesearchengine.google.com/).
2. Clique em **Adicionar**, dê um nome (ex: `LeadHunter Web`) e selecione **Pesquisar em toda a Web**.
3. Copie o **ID do mecanismo de pesquisa (`cx`)** para `GOOGLE_SEARCH_ENGINE_ID`.

### 5. Obter a chave da Gemini API (`GEMINI_API_KEY`)
1. Acesse [aistudio.google.com/apikey](https://aistudio.google.com/apikey).
2. Clique em **Create API Key** e copie a chave para `GEMINI_API_KEY`.

### 6. Criar a Service Account para Google Sheets & Drive
1. No Google Cloud Console, vá em **IAM e administrador → Contas de serviço → + Criar conta de serviço**.
2. Dê um nome (ex: `leadhunter-sheets`) e conclua a criação.
3. Clique na conta criada → aba **Chaves → Adicionar chave → Criar nova chave → JSON**.
4. Salve o arquivo baixado na raiz do repositório como `service-account.json` (já listado no `.gitignore`).
5. Defina seu e-mail pessoal em `GOOGLE_SHEETS_SHARE_EMAIL` para que toda planilha criada seja compartilhada automaticamente com permissão de editor (**ou** informe um `GOOGLE_SHEETS_SPREADSHEET_ID` existente compartilhado com o e-mail da Service Account).

---

## Configuração do `.env`

Copie [.env.example](.env.example) para `.env` na raiz do repositório e preencha suas credenciais:

```env
# 1. Google Places API (New)
GOOGLE_PLACES_API_KEY=AIza...

# 2. Google Custom Search API (cota gratuita 100/dia; teto seguro 95/dia)
GOOGLE_SEARCH_API_KEY=AIza...
GOOGLE_SEARCH_ENGINE_ID=a1b2c3d4e5f6...

# 3. Gemini API (gemini-2.5-flash)
GEMINI_API_KEY=AIza...

# 4. Google Sheets & Drive API (Service Account)
GOOGLE_SERVICE_ACCOUNT_PATH=service-account.json
GOOGLE_SHEETS_SHARE_EMAIL=seu.email@gmail.com
GOOGLE_SHEETS_SPREADSHEET_ID=
```

---

## Comandos da CLI

Na raiz do repositório:

```powershell
# 1. Preview: processa até fechar PreviewBatchSize leads válidos com celular confirmado,
#    exporta para o Google Sheets e exibe projeção de custo para TargetValidLeads
dotnet run --project src/LeadHunter.Cli -- preview --config campaigns/dentistas-abc.json

# 2. Run: executa coleta completa, enriquecimento (Tier 1 -> Tier 2), análise Gemini e exportação
dotnet run --project src/LeadHunter.Cli -- run --config campaigns/dentistas-abc.json

# 3. Resume: retoma queries pendentes e leads marcados como PendingQuota sem repetir gastos
dotnet run --project src/LeadHunter.Cli -- resume --campaign dentistas-abc

# 4. Status: exibe painel consolidado de leads, donos, exportações, descartes e custos
dotnet run --project src/LeadHunter.Cli -- status
```

---

## Estrutura da Planilha Exportada (Google Sheets)

A planilha gerada contém **3 abas**:
1. **`Leads_Prontos`** — Leads válidos com celular confirmado (`+55 DD 9XXXX-XXXX`), ordenados por *sem site primeiro* e depois *só Instagram*, com as 19 colunas prontas para CRM/disparo (`Nome do Contato`, `Nome da Empresa`, `Telefone Principal`, `Telefone 2`, `Endereço com Bairro`, `Site`, `Fonte do Contato`, `Tag`, `Dono`, `Confiança do Dono`, `Fonte do Dono`, `Instagram`, `CNPJ`, `Nota Google`, `Número de Avaliações`, `Tem Site?`, `Link do Google Maps`, `Cliente da Avaliação`, `Serviço Elogiado`).
2. **`Pendentes_Cota`** — Leads aguardando enriquecimento após o limite seguro de 95 buscas/dia.
3. **`Auditoria_e_Custos`** — Total de leads, donos identificados, descartes agrupados por motivo, cota de busca usada no dia, tokens Gemini e custo total estimado em USD.
