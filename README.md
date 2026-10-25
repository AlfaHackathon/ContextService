# ContextService

## Структура кода

Один web-проект `Api` и отдельный `Tests`, как в существующих сервисах организации.
Образцы: [CompanyService](https://github.com/AlfaHackathon/CompanyService/tree/f6c3413c1c6dc536b31fe2a24ad272b6c6c358cd/Api)
и [UserSql resources](https://github.com/AlfaHackathon/UserService/tree/23b88c7cd14a1d4d0b8e07968533f896121c74f7/Api/Resources).

| Путь | Ответственность |
|---|---|
| `Api/Program.cs`, `Extensions` | Композиция приложения, DI, PostgreSQL Dapper settings, общие HTTP clients и worker lifetime |
| `Api/Controllers`, `Middleware` | Настоящие MVC `[ApiController]`/`HttpGet`/`HttpPost`, org `BaseController`, internal authentication и прежний JSON ошибок |
| `Api/Contracts/*` | Wire DTO по материалам, знаниям, поиску, retrieval, изменениям и компаниям |
| `Api/Services` | Материалы и валидация; CompanyService HTTP client; поиск, retrieval и бюджеты; projection orchestration |
| `Api/Repositories` | PostgreSQL материалы через org `IDapperContext`/`QueryObject`, material change feed, evidence inventory и отметки поисковой проекции |
| `Api/Resources`, `Migrations` | PostgreSQL SQL-ресурсы `.resx`, типизированный доступ, FluentMigrator `AddMigrations`/`UseMigrations` |
| `Api/Common`, `Models`, `Workers` | Канонический JSON/validation, внутренние модели, фоновое обновление Qdrant |

Направление зависимостей: HTTP → Services → Repositories/transport. Репозитории используют
Contracts/Common/Resources и не зависят от application-сервисов. SQL исполняется только
репозиториями/миграциями, с прежними параметрами и транзакциями; ресурсы включены в DLL.
`RetrievalClient`, `SearchProjectionService`, `CompanyKnowledgeClient` и repository отметок создаются через DI.
PostgreSQL хранит канонические версии; Qdrant остаётся перестраиваемой поисковой базой. SQLite fallback отсутствует.
Компаниями, группами, профилями людей и разрешениями владеет CompanyService.
ContextService читает их версии через HTTP для поиска и сборки контекста.
`CompanyService__BaseUrl` задаёт корень host CompanyService, например `http://company-service:8080`;
`CompanyService__InternalSecret` переопределяет общий `Internal__Secret` для этих вызовов.
Недоступность владельца возвращает ошибку. Старый `knowledge_revisions` сохраняется миграцией
для сохранности прежних данных, но больше не читается и не записывается приложением.
Каждый Sync проверяет наличие коллекции и сверяет фактические point IDs/hash с каноническими
записями и PostgreSQL-отметками (до 256 IDs за запрос, без vectors). Потеря коллекции или части
точек восстанавливается следующим Sync/worker даже при сохранённых отметках и без перезапуска.
Исправные точки повторно не отправляются; embeddings считаются только для восстанавливаемых
или изменённых записей. Ошибка сверки/записи Qdrant возвращает ошибку, успешный upsert
получает отметку только после подтверждения Qdrant.

Реально используются NuGet `AlfaHackaton.Controllers` 1.4.1, `AlfaHackaton.DapperContext` 1.2.0,
`AlfaHackaton.HttpClient` 1.1.1 и явно подключённый `AlfaHackaton.Result` 1.0.0: controller success проходит
через `BaseController.OkOrProblem`, persistence через `IDapperContext`, Qdrant/inference HTTP через
`IHttpClient<TSettings>.Send`. Raw JSON bytes сохраняют прежние System.Text.Json контракты.
Ожидаемые ошибки валидации, бизнес-правил и конфликтов возвращаются через пакетные
`Result<T>`/`Result` и пакетный `Error`. Контроллеры напрямую наследуют пакетный `BaseController`:
ошибка возвращается как `{errorMessage}`, а заданный `Error.HttpStatusCode` имеет приоритет
над mapping `ErrorType`. Собственных `ApiError` и controller adapter нет.
Ранний возврат ошибки оставляет транзакцию без commit, и disposal откатывает изменения.
Middleware обрабатывает authentication, ошибки HTTP/БД и неожиданные исключения с прежним `{code,message}`.
Настройка `ConnectionStrings__DefaultConnection` обязательна; приложение применяет миграции
до приёма HTTP. База должна быть отдельной для ContextService.

```powershell
# Для integration tests укажите изолированный synthetic PostgreSQL с CREATE DATABASE privileges.
# CONTEXT_TEST_POSTGRES задаётся через окружение; каждый тест создаёт и удаляет собственную БД.
dotnet test ContextService.slnx -c Release
dotnet publish Api/ContextService.Api.csproj -c Release -o ./publish
```

`PostgresStorageTests` проверяет реальные org migrations, reopen, точные ticks и payload, rollback
при сбое change feed, concurrent idempotence, temporal conflicts и atomic projection marks.
`MvcContractTests` выполняет настоящий MVC pipeline; `OrganizationHttpClientTests` — реальный
loopback HTTP через общий пакет, включая JSON bytes, headers, redirects и cancellation.
`SearchProjectionRecoveryTests` проверяет потерю и предварительное пересоздание коллекции
с перезапуском и без, частичную потерю/устаревшие hashes, отсутствие повторных embeddings
для исправных точек и повтор после отказа Qdrant на настоящем PostgreSQL.

## Каталог admitted public news

`POST /context-service/internal/news/search` — внутренний read-only lexical endpoint
для `ClientAssistant` AgentService `b4791f1e5dd609c55379630140a8af5c69674ef2`.
Требует существующий `X-Internal-Secret`; public routes/permissions не добавляются.

```json
{"query":"ключевая ставка","asOf":"2026-10-25T10:00:00.0000001Z","limit":6}
```

Strict JSON request: query nonempty после trim, raw length <=2000 UTF-16 units;
asOf — explicit ISO instant с offset/Z и максимум 7 fractional digits; limit 1..6
(default 6). Company/user IDs, URL acquisition и произвольный scope запрещены.
Body ограничен 16 KiB. Ответ `{asOf,exhaustive:false,hits:[...]}`; hit — canonical
MaterialVersion metadata **без text**, включая точные IDs/hash, dates и provenance.
Отсутствующий provenance остаётся absent/unknown; lead/excerpt не повышаются до full.

SQL выбирает только known_at_ticks<=asOf, kind=news и пустые companyIds/bankProfileIds;
проверяет также relational company scope. Public correction supersedes public ancestors,
в том числе через непубличную промежуточную версию; скрытая версия не становится hit.
Из оставшихся permitted heads выбирается один на material: knownAt DESC, versionId
ordinal DESC при равенстве. Затем выполняется case-insensitive literal substring
matching по title/text: `%`, `_` и SQL punctuation — данные, не wildcard/SQL.
Выбор current version происходит **до** matching, поэтому совпадающий устаревший текст
не возвращается вместо новой версии. Итоговый порядок: knownAt DESC, materialId,
versionId в PostgreSQL `C` collation; максимум limit hits. Неполный каталог не означает
отсутствия влияния новости на компанию.

Поиск не вызывает Qdrant, embedding/rerank, parser, URL acquisition или агента.
Одна read-only транзакция использует server statement_timeout=2000ms. Org DB API не
передаёт CancellationToken внутрь query: cancellation проверяется до/после, а оставшаяся
SQL-работа ограничивается server timeout. Ошибка/timeout не превращаются в пустой успех.
Материалы/журналы не изменяются. Используются существующие таблицы и kind/asOf index;
новых migrations/indexes нет.

`/internal/news/resolve` остаётся прежним exact-version чтением. Agent привязывает hit
к opaque company-bound newsHandle, повторно проверяет scope/version/hash/asOf при read
и передаёт selected version прежнему анализатору. Parser lead observation hash не
является canonical Context version/hash. Каталог не выполняет source admission.

### Проверка и передача deployment executor

Native gates требуют .NET 10, доступ к org GitHub Packages через штатный process
credential из раздела restore и **изолированный synthetic PostgreSQL** с CREATE DATABASE.
В этой cloud-сессии установлен SDK 10.0.402, но restore org feed блокируется proxy HTTP
403. Поэтому API build, xUnit/MVC tests и publish здесь **не подтверждены**. Ни feed
mapping, ни package versions не изменялись. Отдельно проверены embedded SQL на настоящем
локальном PostgreSQL и native DTO serialization/wire compatibility с Agent; это не
замена native gates:

```sh
dotnet restore ContextService.slnx --configfile NuGet.config
dotnet build ContextService.slnx -c Release --no-restore
# CONTEXT_TEST_POSTGRES задаётся только на synthetic instance: тесты создают/удаляют БД.
dotnet test ContextService.slnx -c Release --no-restore
dotnet publish Api/ContextService.Api.csproj -c Release --no-restore -o ./publish
```

`NewsSearchValidationTests`, `NewsSearchTests` и новый MVC case проверяют strict input,
точные ticks, latest-before-match, private/bank/future scopes, stable limits, literal
queries, provenance, auth и совместимость exact read. Все inference tests используют
локальные fake HTTP dependencies; живые модели для этого endpoint не нужны.

Deployment выполнять только после native gates. Prerequisites: прежняя Context DB и
internal-secret wiring, source admission реально наполненного каталога, Agent commit
b4791f1 для новых tools. Рекомендуемый порядок — Context endpoint, затем Agent tools;
parser deployment не входит в этот commit. Изменение additive, без data migration.
Rollback Context возвращает Agent search к explicit unavailable (404 dependency), а
прежние Context/Agent APIs остаются доступны; rollback Agent просто убирает новые tools.
Данные и immutable revisions не требуют отката. Cloud deploy/merge не выполнялись.

## Версии, добор и события

`POST /context-service/internal/context/retrieve` принимает `scope=company|market` и до шести
`requiredMaterialIds` для адресного получения известных оригиналов. Выбираются только разрешённые
company/time версии, с полным текстом и проверяемыми hashes; превышение бюджета возвращает ошибку.
Coverage описывает доступный корпус, а не полноту всех реальных обязательств компании.

Каждая успешная новая material revision записывает `context_changes` в той же PostgreSQL
транзакции. Повтор immutable version нового события не создаёт. `GET /internal/changes?after=N`
возвращает следующую запись; долговечный consumer cursor принадлежит InsightService.
CompanyService ведёт отдельный журнал изменений своих профилей и групп. Потребитель использует
независимые долговечные cursors этих двух журналов. До появления вопросов старые материалы
не запускают массовый LLM-анализ.

Первый .NET 10 срез хранения исходных материалов и сборки контекста компании. Канонические данные живут в PostgreSQL через общий DapperContext. Код не содержит fixtures или готовых выводов. Семантический поиск по всему реестру и реальные документные ACL в этот срез не входят.

## Восстановление пакетов

`NuGet.config` использует штатный GitHub Packages feed организации:
`https://nuget.pkg.github.com/AlfaHackathon/index.json`. Mapping `AlfaHackaton.*`
закрепляет общие пакеты за GitHub; остальные зависимости берутся из nuget.org.
Local/source feed fallback в конфигурации отсутствует.

Для чистого restore задайте credential только в окружении процесса через
`NuGetPackageSourceCredentials_github` в формате
`Username=<github-login>;Password=<PAT-with-read:packages>;ValidAuthenticationTypes=Basic`.
Не сохраняйте token в NuGet.config, appsettings или репозитории.

```powershell
dotnet restore ContextService.slnx --configfile NuGet.config
dotnet build ContextService.slnx -c Release --no-restore
# CONTEXT_TEST_POSTGRES: DSN отдельного synthetic PostgreSQL с CREATE DATABASE privileges.
# Каждый integration test сам создаёт и удаляет уникальную БД; реальные данные не используются.
dotnet test ContextService.slnx -c Release --no-restore
dotnet publish Api/ContextService.Api.csproj -c Release --no-restore -o ./publish
```

## Запуск

```powershell
dotnet build ContextService.slnx
dotnet test ContextService.slnx --no-build
# Настройте Internal__Secret через окружение процесса, не записывая его в репозиторий.
# ConnectionStrings__DefaultConnection — обязательный PostgreSQL DSN через окружение процесса.
dotnet run --no-build --project Api/ContextService.Api.csproj --urls http://127.0.0.1:5181
```

`GET /health` возвращает только service/status. Все `/context-service/internal/*` требуют непустой настроенный `Internal__Secret` в `X-Internal-Secret`; отсутствующее значение конфигурации не открывает доступ. Первый срез использует доверенный внутренний company scope, не выводит права из UID или родительской компании.

## HTTP

Ответы JSON/camelCase; ожидаемые ошибки — пакетный `{errorMessage}` с соответствующим HTTP status.

| Метод и путь | Контракт |
|---|---|
| POST `/context-service/internal/material-versions` | `{materialId,versionId,sourceId,kind,title,text,knownAt,publishedAt?,correctsVersionId?,companyIds:Guid[],bankProfileIds:string[]}` → тот же материал + `contentHash` |
| GET `/context-service/internal/material-versions/{materialId}/{versionId}` | Точная неизменяемая версия или 404 |
| POST `/context-service/internal/context` | `{companyId,newsMaterialId,newsVersionId,asOf}` → `{news,materials,coverage}` |

`kind`: `news`, `company`, `market`. `companyId` — Guid приложения; `bankProfileId` — отдельная строка профиля и не выдаёт доступ. Company-материал требует непустой companyIds. Market-материалы без companyIds общие; при наличии companyIds они ограничены этими компаниями. Нужная news-version запрашивается точно и также проверяется по scope/knowledge time.

Один `(materialId,versionId)` неизменяем. Повтор того же полного канонического payload возвращает прежнюю версию; изменение текста или метаданных даёт 409. Хэш SHA256 включает весь текст и метаданные; UTC и множества идентификаторов нормализуются. SourceId/kind неизменны внутри одного materialId. Correction ссылается на уже существующую версию того же материала и не может быть известна раньше неё. Старые версии сохраняются.

Контекст включает разрешённые на `asOf` company/market-версии и предыдущие версии этой же новости; текущая новость идёт отдельно. `knownAt <= asOf` — обязательная граница. Никакие даты хозяйственной операции сами по себе не удаляют старый документ. `coverage` содержит `complete`, `materialCount` (включая news), `companyMaterialCount`, `marketMaterialCount`, `previousNewsVersionCount`, `characters`, `asOf`.

`Context__MaxCharacters` по умолчанию 120000. Это бюджет сериализованного блока исходных news/materials, включая метаданные; превышение возвращает 422 `context_budget_exceeded` и не выдаёт обрезанный результат. Полнота относится к имеющимся документам в этой базе и не доказывает правильность их прочтения или полноту поиска адресатов.

PostgreSQL использует параметризованные SQL из `.resx` и транзакции организации. Material version, company scopes и change feed коммитятся атомарно. Advisory transaction lock `319264381` сериализует canonical writes; evidence inventory держит shared lock в той же транзакции для согласованности rows/watermarks. Все чтения внутри транзакций идут через `ITransaction.CommandWithResponse`, без открытия второй connection. `known_at_ticks`/`known_ticks` — BIGINT (100 ns), payload — TEXT с прежним JSON и hash. Change sequence допускает пропуски после rollback. Foreign keys `DEFERRABLE INITIALLY IMMEDIATE` позволяют отдельный контролируемый импорт прежних данных; runtime проверка остаётся immediate.

Проверки `Tests/MaterialsTests.cs` покрывают неизменяемость, перезапуск repository, actual correction lineage, знание на дату, company Guid scope, отказ при бюджете и канонизацию. Настоящий межпроцессный HTTP-сценарий выполняется отдельно интеграционным harness; repository tests не объявляются его заменой.

## Текущий поиск, знания и происхождение

Inference HTTP использует независимо настроенные `Embeddings__BaseUrl` и `Reranker__BaseUrl`
(корень API, включая `/v1`, без суффиксов `/embeddings` и `/rerank`).
`Embeddings__Model` и `Reranker__Model` передаются провайдеру без подмены alias:
задайте реальные served model IDs, проверенные оператором у выбранного endpoint.
Имена из Development-конфигурации не доказывают, что модели загружены удалённым сервером.
Для E5-small ожидаются `Embeddings__Dimensions=384`, `Embeddings__QueryPrefix="query: "`
и `Embeddings__PassagePrefix="passage: "`; размер вектора проверяется на ответе.

Опциональные `Embeddings__ApiKey` и `Reranker__ApiKey` задаются через окружение/secret store
и отправляются как Bearer только в соответствующем запросе. Пустое значение не добавляет
Authorization; embedding-ключ не используется как fallback для rerank или Qdrant.
Не записывайте реальные ключи в файлы репозитория.

Для Infinity задайте `Reranker__RawScores=true`, чтобы запросить raw logits: в его
[официальной схеме](https://github.com/michaelfeil/infinity/blob/main/libs/infinity_emb/infinity_emb/fastapi_schemas/pymodels.py)
`raw_scores` по умолчанию false. Если настройка отсутствует, поле не отправляется,
сохраняя прежний запрос для других совместимых провайдеров. Значения score, включая
отрицательные, сохраняются без sigmoid/нормализации и сортируются по убыванию;
это не вероятности. Rerank получает исходный query и тексты без E5-префиксов,
`top_n` равен всему переданному набору кандидатов. Ответ сопоставляется с исходными
документами по `index`, затем применяется пользовательский limit. Повторные,
пропущенные/неверные индексы, неверные размерности и нечисловые/бесконечные значения
дают 502; HTTP-отказ upstream даёт 503 без включения response body в ошибку.

`POST /internal/context/retrieve` выполняет bounded retrieval загруженных разрешённых оригиналов с `companyId/asOf/queries/limit`, optional `scope=company|market` и `requiredMaterialIds` (до 6). Новейшая известная версия выбранного материала возвращается рядом с историческим попаданием; все версии с равным максимальным knownAt сохраняются для сравнения, opaque version ID не задаёт временной порядок. Превышение бюджета даёт 422 без обрезки. `/internal/search` — выбранный Qdrant, lexical/dense RRF и настроенный reranker; `relatedVersions` у попадания содержит точные refs новых известных версий, отдельно от refs текста старого excerpt. `/internal/search/sync` восстанавливает индекс из канонических материалов и профилей.

Совместимые `/internal/companies`, `/internal/groups`, `/internal/roles`, `/internal/people` передают запросы в CompanyService `/company-service/internal/knowledge/*`. Проверка версий, раскрытие групп и read grants выполняются владельцем. Новые клиенты обращаются к CompanyService напрямую. ContextService сохраняет материалы и их evidence; `/internal/changes?after=` выдаёт только журнал материалов.

`POST /internal/news/admit` принимает событие `{source,externalId,title,text,publishedAt}` с optional
`knownAt`, `correctsVersionId`, `textCompleteness` (по умолчанию `lead`) и `sourceUrl`.
ContextService определяет прежние SHA-идентификаторы публикации/версии и возвращает
`{newsMaterialId,newsVersionId,asOf,textCompleteness}`. Повтор и конкурирующая доставка используют
сохранённый первый `knownAt`. Для совместимости идентичности хешируется исходная строка `publishedAt`
и точный JSON.stringify-эквивалент строк, без замены Unicode/HTML на другие escape-последовательности.
`POST /internal/news/resolve` с `{newsMaterialId,newsVersionId,asOf}` возвращает публичный оригинал
новости, известный на срез, или 422. `POST /internal/news/public-sources` с `{sourceRefs,asOf}`
возвращает `{materials}` для максимум 64 публичных market-оригиналов после проверки hash/knownAt/scope.
Размер модельного контекста, окна и source-coverage остаются ответственностью AgentService.

Material version может включать optional `provenance={acquisition,sourceUrl,retrievedAt,textCompleteness,originVersionId,locator}`. Acquisition: `uploaded`, `event_payload`, `news_service`; полнота: `full`, `lead`, `excerpt`, `unknown`. Для загруженного локального PDF укажите uploaded/excerpt и страницу/OCR в locator: это не результат интернет-поиска. RetrievedAt не может быть позже knownAt; ссылка допускает только HTTP(S) без credentials. Для news_service требуется тот же originVersionId, что и versionId проекции; оригинальные версии не перенумеровываются ContextService. Получение таких оригиналов требует реального upstream контракта — существующий parser его пока не предоставляет. Исторические записи без provenance остаются без него; null не меняет старый canonical hash. Изменение метаданных существующей версии запрещено как изменение текста.
