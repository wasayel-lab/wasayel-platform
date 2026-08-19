# ══════════════════════════════════════════════════════════════════════
#  صورَة النَشر على Hugging Face Spaces (‏sdk: docker، المَنفَذ 7860)
# ══════════════════════════════════════════════════════════════════════
#
#  **النَسَب**: هذا المِلَفّ مَنقول مِن `acommerce-lab/acommerce-platform`
#  فَرع `deploy-hf` — وهو المِلَفّ الَّذي يَبني الإنتاج الحَيّ اليَوم.
#  والفَرق المَقصود واحِد: **بادِئَة المَسار**. هُناك كانَ المَشروع تَحتَ
#  `platform-v1/`، وهُنا استَخرَجَت الشَجَرَةُ الجُذورَ فَصارَ `apps/`
#  و`libs/` في الجَذر. فَـ`platform-v1/apps/V1.App/V1.App.csproj` صارَ
#  `apps/V1.App/V1.App.csproj`، و`COPY platform-v1/ ./platform-v1/` صارَ
#  `COPY . .`. ولا شَيءَ سِواه تَغَيَّر: نَفس الصورَتَين الأَساسِيَّتَين،
#  ونَفس المَنفَذ، ونَفس مُتَغَيِّرات البيئَة، ونَفس الـ ENTRYPOINT.
#
#  **لَم تُبنَ هذِه الصورَة**: لا Docker على جِهاز التَطوير (‏`docker
#  --version` → command not found). فَالتَحَقُّق قِراءَةٌ ومُقابَلَةٌ
#  بِالشَجَرَة **وقِياسٌ على مُخرَج `dotnet publish` مَحَلِّيّاً** — وهو
#  يُثبِت الأَمر لا الحاوِيَة. والتَصريحُ بِهذا الحَدّ في `docs/DEPLOY.md`.
#
#  ما قيسَ فِعلاً (لا ما ظُنَّ):
#
#   ١. `apps/V1.App/V1.App.csproj` قائِم، وفيه **41 إحالَةَ مَشروع
#      مُباشِرَة** بِمَسارات `..\..\libs\…` كُلُّها مَوجودَة. فَـ`restore`
#      عَلى المَشروع وَحدَه يَمشي الرَسمَ البَيانِيّ بِلا حاجَةٍ إلى
#      `PlatformV1.slnx`. والدَليل: `dotnet publish` بِهذا المَسار نَفسِه
#      نَجَحَ مَحَلِّيّاً وأَخرَجَ **50 تَجميعَة `ACommerce*.dll`**.
#   ٢. **الموارِد المُضَمَّنَة تَدخُل الـDLL فَلا تُنسَخ — مَقيسَةً مِن
#      المُخرَج نَفسِه**: قُرِئَت جَداوِلُ `ManifestResources` في تَجميعات
#      النَشر فَأَعطَت **32 مَورِداً في 4 تَجميعات**: ‏11 دَوراً في
#      `Roles.Core`، و4 في `Theme.Core`، و10 تَدَفُّقات في `Flows`، و7 في
#      قالِب السوق مِنها `I18n.Locales.ar.json` و`en.json`. فَلا `COPY`
#      لِـ`Definitions/` ولا لِـ`I18n/`. (وسَنَدُ التَصريح: 9 تَصريحات
#      `EmbeddedResource` في ‏4 مَشاريع.)
#   ٣. `scripts/` و`docs/` لا يَقرَؤُهُما التَطبيق وَقتَ التَشغيل. المَسحُ
#      عَلى `File.*`/`Directory.*` في `apps/` و`libs/` أَعطى: مَسارَ
#      `wwwroot/uploads` (‏يُنشِئُه مُنشِئُ `LocalFileStorage` بِنَفسِه)،
#      ومَلَفَّي اعتِمادٍ لِمُزَوِّدَي GoogleCloud/Firebase يَأتي مَسارُهُما
#      **مِن التَهيئَة لا مِن الشَجَرَة** ولا يُفَعَّلانِ افتِراضِيّاً.
#      فَـ`scripts/` و`docs/` مَحجوبانِ في `.dockerignore`.
#   ٤. `wwwroot` يَخرُج مِن `publish` فِعلاً: **67 مِلَفّاً** — مِلَفّا
#      `branding/` المُتَتَبَّعانِ، و19 أَصلاً ساكِناً مِن المَكتَبات تَحتَ
#      `_content/`، مَع نُسَخِها `.br`/`.gz`.
#   ٥. البِناءُ عَلى Linux مُثبَت لا مَظنون: بَوّابَةُ CI تَبني الحَلَّ
#      كامِلاً عَلى `ubuntu-latest` وتَمُرّ — فَالشَرطَة المَقلوبَة في
#      `EmbeddedResource Include="Definitions\**\*.json"` يُسَوّيها
#      MSBuild هُناك فِعلاً، ولَيسَ هذا استِنتاجاً مِن الوَثائِق.
#
# ══════════════════════════════════════════════════════════════════════

# --- مَرحَلَة البِناء (.NET 10 SDK رَسميّ مِن Microsoft) ---------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src
COPY . .

RUN dotnet restore apps/V1.App/V1.App.csproj --nologo

# نَشر إنتاجيّ (Release). لا trimming لِأَنّ Blazor SSR
# يَستَخدِم reflection في بَعض الـ kits.
RUN dotnet publish apps/V1.App/V1.App.csproj \
        -c Release \
        -o /publish \
        --no-restore \
        --nologo

# --- مَرحَلَة التَّشغيل (ASP.NET Core 10 runtime رَسميّ) --------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app
COPY --from=build /publish ./

# Hugging Face Spaces يَتَوَقَّع المَنفَذ 7860.
ENV ASPNETCORE_URLS=http://+:7860
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 7860

ENTRYPOINT ["dotnet", "V1.App.dll"]
