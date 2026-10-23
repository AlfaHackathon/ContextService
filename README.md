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

`POST /internal/context/retrieve` выполняет bounded retrieval загруженных разрешённых оригиналов с `companyId/asOf/queries/limit`, optional `scope=company|market` и `requiredMaterialIds` (до 6). Новейшая известная версия выбранного материала возвращается рядом с историческим попаданием; все версии с равным максимальным knownAt сохраняются для сравнения, opaque version ID не задаёт временной порядок. Превышение бюджета даёт 422 без обрезки. `/internal/search` — выбранный Qdrant, lexical/dense RRF и настроенный reranker; `relatedVersions` у попадания содержит точные refs новых известных версий, отдельно от refs текста старого excerpt. `/internal/search/sync` восстанавливает индекс из канонических материалов и профилей.

Совместимые `/internal/companies`, `/internal/groups`, `/internal/roles`, `/internal/people` передают запросы в CompanyService `/company-service/internal/knowledge/*`. Проверка версий, раскрытие групп и read grants выполняются владельцем. Новые клиенты обращаются к CompanyService напрямую. ContextService сохраняет материалы и их evidence; `/internal/changes?after=` выдаёт только журнал материалов.

Material version может включать optional `provenance={acquisition,sourceUrl,retrievedAt,textCompleteness,originVersionId,locator}`. Acquisition: `uploaded`, `event_payload`, `news_service`; полнота: `full`, `lead`, `excerpt`, `unknown`. Для загруженного локального PDF укажите uploaded/excerpt и страницу/OCR в locator: это не результат интернет-поиска. RetrievedAt не может быть позже knownAt; ссылка допускает только HTTP(S) без credentials. Для news_service требуется тот же originVersionId, что и versionId проекции; оригинальные версии не перенумеровываются ContextService. Получение таких оригиналов требует реального upstream контракта — существующий parser его пока не предоставляет. Исторические записи без provenance остаются без него; null не меняет старый canonical hash. Изменение метаданных существующей версии запрещено как изменение текста.
