# مَفاتيحُ الوَكيل — الأَربَعَةُ بِحَرفِها، وفَحصٌ قَبلَ الإلصاق

> **الغَرَض**: صَفحَةٌ واحِدَةٌ تُجيبُ عَن «لِماذا يَرُدُّ الوَكيلُ ‏401؟»
> بِلا فَتحِ كود. المِعمارِيَّةُ كامِلَةً في [`AGENT-TOOLS.md`](AGENT-TOOLS.md) §١،
> وقائِمَةُ المُزَوِّدينَ في [`LLM-ALTERNATIVES.md`](LLM-ALTERNATIVES.md)،
> وأَسماءُ أَسرارِ الـSpace في [`DEPLOY.md`](DEPLOY.md) §٦.

---

## ١. العِلَّةُ المَقيسَةُ الَّتي كَتَبَت هذِه الصَفحَة (‏2026-08-31)

رِحلَةُ عَميلٍ حَيَّةٍ على الـSpace تَوَقَّفَت عِندَ **الخُطوَةِ الثالِثَةِ
مِن ثَمان** — «حَلِّل فِكرَتي» — بِـ:

```
فَشِلَ التَحليل
OpenAI 401: {"message":"Invalid API Key","code":"invalid_api_key"}
```

وخَلفَها سَبعُ خُطُواتٍ مَحجوبَة: نُقطَةُ بِناءِ المَتجَرِ تَشتَرِطُ اكتِمالَ
التَحليل ⇒ لا مَتجَر، ولا ضَبط، ولا اشتِراك، ولا طَلَب. **فَمِفتاحٌ واحِدٌ
يَقِفُ أَمامَ المُنتَجِ كُلِّه.**

**والرِسالَةُ نَفسُها كانَت نِصفَ العَطَب**: بادِئَةُ `OpenAI` فيها اسمُ
**الصَنف** (`OpenAIBackend`) لا اسمُ **الخادِم**؛ والصَنفُ نَفسُه يَخدِمُ
‏Groq وCerebras وOpenRouter وOllama وأَيَّ مُتَوافِقٍ آخَر. فَالرِسالَةُ
تَقولُ ما رَدَّهُ الخادِمُ ولا تَقولُ **أَيَّ خادِمٍ نودِيَ**. ولِذلك
أُضيفَ سَطرُ الإقلاعِ في §٥.

---

## ٢. المُتَغَيِّراتُ الأَربَعَةُ بِحَرفِها

الـSpace يَقرَأُ التَهيئَةَ بِنَمَطِ الشَرطَتَينِ السُفلِيَّتَينِ
المُضاعَفَتَين: ‏`.NET` يُتَرجِمُ `A__B` إلى `A:B`.

| المُتَغَيِّر (‏بيئَة) | يُقابِلُ (‏تَهيئَة) | القيمَة | إلزامِيّ؟ |
|---|---|---|---|
| `Agent__Provider` | `Agent:Provider` | `anthropic` \| `gemini` \| `openai` | نَعَم عَمَلِيّاً |
| `Agent__BaseUrl` | `Agent:BaseUrl` | عُنوانُ المُزَوِّدِ المُتَوافِقِ **بِلا `/v1`** | **نَعَم مَعَ `openai`** |
| `Agent__ApiKey` | `Agent:ApiKey` | المِفتاح | نَعَم |
| `Agent__Model` | `Agent:Model` | مُعَرِّفُ النَموذَج | لا (‏فَافتِراضِيُّ الخَلفيَّة) |

وخامِسٌ اختِيارِيّ: `Agent__ProviderLabel` — تَسميَةٌ صَريحَةٌ لِلمُزَوِّد
تَظهَرُ في اللوغ بَدَلَ الاستِنتاجِ مِنَ العُنوان.

**ولِكُلِّ وَكيلٍ مَنطِقِيٍّ نُسخَتُه** إن أُريدَ الفَصل — تَسبِقُ العامَّةَ:

```
Agents__Analysis__Provider   Agents__Analysis__BaseUrl   Agents__Analysis__ApiKey   Agents__Analysis__Model
Agents__Studio__Provider     Agents__Studio__BaseUrl     Agents__Studio__ApiKey     Agents__Studio__Model
```

`Analysis` هُوَ مُحَرِّكُ دِراسَةِ الجَدوى (‏الخُطوَةُ الثالِثَة)،
و`Studio` هُوَ وَكيلُ الأَدَواتِ الثَمان.

**قاعِدَةُ السُقوط** (‏`AgentProfileResolver.Resolve`) — أَوَّلُ قيمَةٍ
**غَيرِ فارِغَة** تَفوز، والفَراغُ والمَسافاتُ تُعامَلُ كَغِياب:

```
Agents:{Name}:{Key}   ←   Agent:{Key}   ←   مُتَغَيِّرُ بيئَةٍ لِلمِفتاحِ وَحدَه
```

### الفَخُّ الَّذي يَصنَعُ ‏401 — مَقروءاً مِنَ الكود

السُقوطُ إلى مُتَغَيِّرِ البيئَةِ **يَشمَلُ `ApiKey` وَحدَه**
(‏`AgentProfileResolver.EnvApiKey`)، **ولا يَشمَلُ `BaseUrl` إطلاقاً**:

| `Provider` المَحلول | مُتَغَيِّراتُ المِفتاحِ الَّتي تُقرَأُ بِالتَرتيب |
|---|---|
| `anthropic` (‏الافتِراضيّ) | `ANTHROPIC_API_KEY` |
| `gemini` | `GEMINI_API_KEY` ثُمَّ `GOOGLE_API_KEY` |
| `openai` | `OPENAI_API_KEY` ثُمَّ `GROQ_API_KEY` ثُمَّ `CEREBRAS_API_KEY` ثُمَّ `OPENROUTER_API_KEY` |

فَلَو ضُبِطَ `Agent__Provider=openai` ومِفتاحٌ بِأَيِّ اسمٍ مِنَ الأَربَعَة،
**وغابَ `Agent__BaseUrl`**، فَـ`OpenAIBackend` يَسقُطُ إلى
`https://api.openai.com/` (‏`AgentBackends.cs`، مُنشِئُ `OpenAIBackend`) —
فَيُرسَلُ مِفتاحُ Groq أَو Cerebras أَو OpenRouter إلى خَوادِمِ OpenAI،
فَتَرُدُّ ‏**401 مُصادَقَة**. والقارِئُ يَظُنُّها **حِصَّةً نافِدَة** وهي
لَيسَت. **العِلاجُ سَطرٌ واحِد: اضبِط `Agent__BaseUrl` مَعَ كُلِّ
`Provider=openai`، دائِماً.**

### و`BaseUrl` لا تَحمِلُ `/v1`

`OpenAIBackend` يُلحِقُ `v1/chat/completions` بِالعُنوانِ بَعدَ تَطبيعِه
(`TrimEnd('/') + "/"`). فَعُنوانٌ يَنتَهي بِـ`/v1` يُنتِجُ
`…/v1/v1/chat/completions` ⇒ **404**، لا 401. (‏جَدوَلُ §٦.)

---

## ٣. مِن أَينَ يُؤخَذُ المِفتاح

> **وهذا لا يُفعَلُ نيابَةً عَن صاحِبِ المَشروع** — إنشاءُ الحِساباتِ
> والمَفاتيحِ فِعلُه وَحدَه. ما دونَه هُنا خُطُواتُ دَلالَةٍ لا تَنفيذ.

| المُزَوِّد | الخُطوَة | شَكلُ المِفتاح |
|---|---|---|
| **Groq** | `console.groq.com` ← ‏API Keys ← ‏Create API Key | `gsk_…` |
| **Cerebras** | `cloud.cerebras.ai` ← ‏API Keys | `csk-…` |
| **OpenRouter** | `openrouter.ai/keys` | `sk-or-…` |
| **DeepSeek** | `platform.deepseek.com` ← ‏API keys | `sk-…` |
| **Anthropic** | `console.anthropic.com` ← ‏API Keys | `sk-ant-…` |
| **Gemini** | `aistudio.google.com/apikey` | سِلسِلَةٌ بِلا بادِئَة |

### ‏GitHub Models — **مُتَقاعِدٌ، لا يُبنى عَلَيه**

**‏GitHub Models أُغلِقَ نِهائِيّاً في ‏2026-07-30.** لَيسَ حِصَّةً نَفِدَت
ولا مِفتاحاً بَطَل — الخِدمَةُ نَفسُها زالَت:

- ‏`https://models.github.ai/inference/chat/completions` يَرُدُّ **‏410 Gone**
  بِـ`{"error":{"code":"github_models_retirement_brownout",…}}` — **بِلا رَمزٍ
  وبِرَمزٍ مُزَيَّفٍ سَواء**، فَالبَوّابَةُ قَبلَ المُصادَقَة. (‏قيسَ 2026-08-31.)
- والعُنوانُ الأَقدَم `models.inference.ai.azure.com` **لا يُحَلُّ في DNS** أَصلاً.
- والتَوثيقُ كُلُّه صارَ إشعارَ تَقاعُد:
  [docs.github.com/en/github-models](https://docs.github.com/en/github-models) ·
  [الإعلان](https://github.blog/changelog/2026-07-30-github-models-is-now-retired/).

**فَـ‏401 الَّذي رَأَيناهُ لا يُمكِنُ أَن يَكونَ مِن GitHub Models** — لَو
كانَ العُنوانُ إلَيه لَكانَ الرَدُّ ‏410. والمَثَلُ الثُنائِيُّ في
[`AGENT-TOOLS.md`](AGENT-TOOLS.md) §١ صارَ **دَيناً تارِيخِيّاً** لا وَصفَةً.

وحَتّى قَبلَ التَقاعُد لَم يَكُن يَصلُحُ لِلتَحليلِ الطَويل: السَقفُ كانَ
**‏8000 رَمزَ دَخلٍ و4000 خَرجٍ لِلطَلَب** مَهما اتَّسَعَ سِياقُ النَموذَج،
و**‏50–150 طَلَباً في اليَوم** (‏الطَبَقَةُ العُليا/الدُنيا، حِسابُ Copilot
Free/Pro). وتَوثيقُه كانَ يَقولُ صَراحَةً إنّ الحِصَصَ المَجّانِيَّةَ
«‏intended to help you get started with experimentation».

---

## ٤. فَحصٌ مِن سَطرٍ واحِد — **قَبلَ** وَضعِ القيمَتَينِ في الـSpace

يُنادي **نَفسَ المَسارِ** الَّذي يَبنيه `OpenAIBackend` حَرفاً:
`{BaseUrl}/v1/chat/completions`.

**بَاش:**

```bash
BASE=https://api.groq.com/openai; KEY=…; MODEL=openai/gpt-oss-120b; \
curl -sS -w '\n→ HTTP %{http_code}\n' "${BASE%/}/v1/chat/completions" \
  -H "Authorization: Bearer $KEY" -H 'Content-Type: application/json' \
  -d "{\"model\":\"$MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"قل: تمام\"}],\"max_completion_tokens\":8}"
```

**بَاوَرشِل** (‏جِهازُ التَطويرِ هُنا وِيندوز):

```powershell
$Base='https://api.groq.com/openai'; $Key='…'; $Model='openai/gpt-oss-120b'
try { Invoke-RestMethod "$($Base.TrimEnd('/'))/v1/chat/completions" -Method Post `
  -Headers @{Authorization="Bearer $Key"} -ContentType 'application/json' `
  -Body (@{model=$Model;messages=@(@{role='user';content='قل: تمام'});max_completion_tokens=8}|ConvertTo-Json -Depth 5) }
catch { $_.Exception.Response.StatusCode.value__; $_.ErrorDetails.Message }
```

**‏200 ⇒ الثُلاثِيُّ صَحيح.** وما دونَه يُقرَأُ مِن جَدوَلِ §٦.

> **ولا يُلصَقُ مِفتاحٌ في تارِيخِ صَدَفَةٍ ولا في مِلَفٍّ يُودَع.**
> `appsettings.Local.json` مُستَثنىً مِنَ Git، والـSpace يَأخُذُها
> **سِرّاً** لا مُتَغَيِّراً عادِيّاً (‏`DEPLOY.md` §٤).

---

## ٥. سَطرُ الإقلاع — الوِجهَةُ تُقالُ بِاسمِها

مُنذُ ‏2026-08-31 يَطبَعُ `Program.cs` عِندَ كُلِّ إقلاعٍ سَطراً لِكُلِّ
وَكيلٍ مَنطِقِيّ:

```
[agent] Analysis: المُزَوِّد=openai · التَسميَة=groq · العُنوان=https://api.groq.com/openai/ · النَموذَج=openai/gpt-oss-120b · المِفتاح=مَضبوط
[agent] Studio:   المُزَوِّد=openai · التَسميَة=openai · العُنوان=https://api.openai.com/ · النَموذَج=gpt-4o · المِفتاح=مَضبوط
```

- **العُنوانُ مَقروءٌ مِن `HttpClient.BaseAddress` نَفسِه** (‏`IAgentBackend.Endpoint`)
  لا مُعاداً بِناؤُه مِن الإعدادات — فَهُوَ ما سَيُنادى فِعلاً لا ما نَظُنُّه.
- **و`المِفتاح` نَعَم/لا فَقَط** — لا تُطبَعُ قيمَةٌ ولا جُزءٌ مِنها، أَبَداً.
- والسَطرُ الثاني أَعلاهُ هُوَ العَطَبُ بِعَينِه لَو ظَهَر: `openai` بِلا
  `BaseUrl` ⇒ العُنوانُ `api.openai.com` بِالغِياب.

قِراءَتُه على الـSpace: ‏Logs ← ابحَث `[agent]`.

### ٥·ب. والرِسالَةُ على الشاشَةِ تَقولُها أَيضاً — بِلا سِجِلٍّ ولا وُصول

سَطرُ الإقلاعِ يُقرَأُ في **سِجِلِّ الحاوِيَة**، ولا يَبلُغُه مَن يَرى
الشاشَة. فَمُنذُ ‏2026-08-31 صارَت **رِسالَةُ الخَطَأِ نَفسُها** تَحمِلُ
التَسميَةَ والعُنوان:

```
قَبل:  OpenAI 401: {"message":"Invalid API Key","code":"invalid_api_key"}
بَعد:  groq (https://api.groq.com/openai/) 401: {"message":"Invalid API Key","code":"invalid_api_key"}
```

- البادِئَةُ كانَت **اسمَ الصَنفِ الَّذي سَأَل** (`OpenAIBackend`) لا اسمَ
  **الخادِمِ الَّذي رَدّ**؛ والصَنفُ نَفسُه يَخدِمُ Groq وCerebras
  وOpenRouter وOllama. فَجَدوَلُ بَصَماتِ ‏401 في §٦ **لَم يَعُد
  ضَرورِيّاً** لِمَعرِفَةِ الخادِم — يَبقى لِتَمييزِ «مِفتاحٌ باطِل» مِن
  «مِفتاحُ مُزَوِّدٍ آخَر».
- **ولا مِفتاحَ في الرِسالَةِ ولا جُزءٌ مِنه** — تَسميَةٌ وعُنوانٌ ورَمزٌ
  وجِسمُ الخادِمِ مَقصوصاً، لا غَير.
- **والمَوضِعُ واحِد**: `AgentErrorText` في `AgentBackends.cs` — والخَلفِيّاتُ
  الثَلاثُ تَقرَؤُه في سَبعَةِ مَخارِجَ (‏رَدٌّ غَيرُ ناجِح · استِثناء ·
  رَدٌّ مُشَوَّه). والعَقدُ مَحروسٌ سُلوكِيّاً لا نَصِّيّاً:
  `AgentBackendErrorVoiceTests` يُشَغِّلُ `CallAsync` نَحوَ خادِمٍ
  مَحَلِّيٍّ يَرُدُّ ‏401 بِجِسمِ العِلَّةِ نَفسِه، ويَفشَلُ إن عادَت
  البادِئَةُ حَرفِيَّةً.

---

## ٦. جَدوَلُ التَشخيص

| الرَمز | ما يَعنيه هُنا | الفَحصُ الأَوَّل |
|---|---|---|
| **‏401** | **مُصادَقَة، لا حِصَّة.** المِفتاحُ غائِبٌ أَو مُبطَلٌ أَو **مُرسَلٌ إلى خادِمٍ لا يَعرِفُه** | الرِسالَةُ نَفسُها تَقولُ التَسميَةَ والعُنوان (§٥·ب)؛ وسَطرُ `[agent]` يُؤَكِّد: أَيُطابِقُ العُنوانُ مُصدِرَ المِفتاح؟ |
| **‏404** | المَسارُ لا النَموذَج — أَو `BaseUrl` تَحمِلُ `/v1` فَتَضاعَفَت | أَزِل `/v1` مِنَ العُنوان؛ ثُمَّ تَحَقَّق مِن `Model` عِندَ المُزَوِّد |
| **‏429** | **الحِصَّةُ فِعلاً** — طَلَباتٌ في الدَقيقَة/اليَوم | اقرَأ رَأسَي `retry-after` و`x-ratelimit-*` في الرَدّ، وانتَظِر أَو بَدِّل مُزَوِّداً |
| **‏410** | الخِدمَةُ زالَت — وهذا حالُ GitHub Models مُنذُ ‏2026-07-30 | بَدِّل المُزَوِّدَ كامِلاً (§٣، §٧) |
| **‏400** | الجِسمُ لا المِفتاح — نَموذَجٌ مَجهولٌ أَو حَقلٌ غَيرُ مَدعوم | جَرِّب المُعَرَّفَ في أَمرِ §٤ وَحدَه |

### وأَشكالُ ‏401 مَقيسَةً — تَقولُ أَيَّ خادِمٍ نودِيَ

قيسَت بِمِفتاحٍ مُزَيَّفٍ في ‏2026-08-31. **الشَكلُ بَصمَةٌ**: يُقارَنُ
بِالرِسالَةِ الظاهِرَةِ فَيُعرَفُ الخادِمُ بِلا أَيِّ وُصولٍ لِلـSpace.

| الخادِم | جِسمُ ‏401 |
|---|---|
| `api.openai.com` | `{"error":{"message":"Incorrect API key provided: sk-thisi********lkey. …","type":"invalid_request_error","code":"invalid_api_key","param":null},"status":401}` |
| `api.groq.com` | `{"error":{"message":"Invalid API Key","type":"invalid_request_error","code":"invalid_api_key"}}` |
| `api.cerebras.ai` | `{"message":"Wrong API Key","type":"invalid_request_error","param":"api_key","code":"wrong_api_key"}` |
| `openrouter.ai` | `{"error":{"message":"Missing Authentication header","code":401}}` |
| `api.deepseek.com` | `{"error":{"message":"Authentication Fails, Your api key: ****lkey is invalid",…}}` |
| `api.mistral.ai` | `{"detail":"Invalid API Key"}` |
| `api.anthropic.com` | `{"error":{"code":"authentication_error","message":"Invalid Anthropic API Key",…}}` |
| `models.github.ai` | **‏410** `{"error":{"code":"github_models_retirement_brownout",…}}` |

**والقِراءَةُ الحاسِمَةُ لِلعَطَبِ المَرصود**: ‏`api.openai.com` **يُصَدِّرُ
دائِماً** جِسماً مُغَلَّفاً بِـ`"error"` **ويُعيدُ صَدى المِفتاحِ مُقَنَّعاً**
(قيسَ بِثَلاثِ حالاتٍ: مِفتاحٌ بِشَكلِ `sk-`، ومِفتاحٌ بِشَكلِ
`github_pat_`، ومِفتاحٌ فارِغ). فَالجِسمُ المَرصودُ — `{"message":"Invalid
API Key","code":"invalid_api_key"}` — **لَيسَ جِسمَ OpenAI**. ورِسالَتُه
ورَمزُه يُطابِقانِ **Groq** حَرفاً. أَي أَنّ `BaseUrl` **مَضبوطٌ ولَيسَ
غائِباً**، والمِفتاحُ هُوَ الباطِل. وسَطرُ §٥ يَحسِمُها في إقلاعٍ واحِد.

---

## ٧. البَدائِلُ — أَينَ تُوضَعُ في هذِه المَفاتيح

الجَدوَلُ الكامِلُ بِالأَسعارِ والحِصَصِ في
[`LLM-ALTERNATIVES.md`](LLM-ALTERNATIVES.md). وما يَعني هذِه الصَفحَةَ
هُوَ **شَكلُ التَهيئَةِ لِكُلٍّ**:

| البَديل | `Provider` | `BaseUrl` | مَجّانِيّ؟ | السِعر المَنشور (‏$/مَليون: دَخل / خَرج) |
|---|---|---|---|---|
| **Gemini** | `gemini` | **يُترَك فارِغاً** | طَبَقَةٌ مَجّانِيَّة — **لا تَصلُحُ إنتاجاً**، انظُر أَدناه | `gemini-2.5-flash-lite` ‏0.10 / 0.40 (‏دُفعَةً: ‏0.05 / 0.20) |
| **DeepSeek** | `openai` | `https://api.deepseek.com` | لا | `deepseek-v4-flash` ‏0.22 / 0.66 خارِجَ الذُروَة · **‏0.007 عِندَ إصابَةِ الكاش** |
| **Groq** | `openai` | `https://api.groq.com/openai` | نَعَم، وضَيِّقَةٌ جِدّاً | `gpt-oss-20b` ‏0.075 / 0.30 · `gpt-oss-120b` ‏0.15 / 0.60 |
| **OpenRouter** | `openai` | `https://openrouter.ai/api` | نَماذِجُ `:free` بِسَقفٍ يَوميّ | `qwen3.7-flash` ‏0.03 / 0.13 · `gpt-oss-20b` ‏0.03 / 0.13 |
| **Mistral** | `openai` | `https://api.mistral.ai` | رَصيدٌ شَهرِيّ، حُدودُهُ غَيرُ مَنشورَة | `Mistral Small 4` ‏0.15 / 0.60 · `Large 3` ‏0.50 / 1.50 |
| **Cerebras** | `openai` | `https://api.cerebras.ai` | تَجرِبَةٌ بِـ$5 · ‏5 طَلَبات/د | `gpt-oss-120b` ‏0.35 / 0.75 |
| **OpenAI** | `openai` | **يُترَك فارِغاً** | لا | `gpt-5.6-luna` ‏0.20 / 1.20 (‏والسِياقُ الطَويلُ ضِعف) |
| **Anthropic** | `anthropic` | **يُترَك فارِغاً** | لا | `Haiku 4.5` ‏1.00 / 5.00 · `Sonnet 5` ‏2.00 / 10.00 (‏كاش ‏0.1×) |
| **Ollama مَحَلِّيّ** | `openai` | `http://localhost:11434` | مَجّانِيٌّ تَماماً | — (‏`ApiKey` يُتَجاهَل) |

<sub>قيسَ ‏2026-08-31 مِن صَفَحاتِ المُزَوِّدينَ المَنشورَة:
[Gemini](https://ai.google.dev/gemini-api/docs/pricing) ·
[DeepSeek](https://api-docs.deepseek.com/quick_start/pricing) ·
[Groq](https://console.groq.com/docs/rate-limits) ·
[OpenRouter](https://openrouter.ai/docs/api-reference/limits) ·
[Mistral](https://mistral.ai/pricing/api) ·
[Cerebras](https://www.cerebras.ai/pricing) ·
[OpenAI](https://developers.openai.com/api/docs/pricing) ·
[Anthropic](https://platform.claude.com/docs/en/about-claude/pricing).
الأَسعارُ تَتَغَيَّر — التارِيخُ جُزءٌ مِنَ الرَقَم.</sub>

### وثَلاثَةُ قُيودٍ تَجارِيَّةٍ تُبطِلُ «المَجّانِيّ» هُنا

هذِه مِنَصَّةٌ تَخدِمُ عُملاءَ يَدفَعون، فَالحِصَّةُ المَجّانِيَّةُ لا تُقاسُ
بِسِعرِها وَحدَه:

1. **طَبَقَةُ Gemini المَجّانِيَّةُ تَتَدَرَّبُ على بَياناتِنا.** صَفحَةُ
   التَسعيرِ تَقولُ لِكُلِّ نَموذَج: `Used to improve our products: Free tier
   = Yes, Paid tier = No`، والشُروطُ تُضيفُ أَنّ «‏human reviewers may read,
   annotate, and process your API input and output». **وتَمنَعُها صَراحَةً
   على مُستَخدِمي المِنطَقَةِ الاقتِصادِيَّةِ الأوروبِيَّةِ وسويسرا
   والمَملَكَةِ المُتَّحِدَة**: «‏You may use only Paid Services when making
   API Clients available to users in the European Economic Area, Switzerland,
   or the United Kingdom» ([الشُروط](https://ai.google.dev/gemini-api/terms)).
   **والطَبَقَةُ المَدفوعَةُ نَظيفَة** — فَالقَيدُ على المَجّانِيَّةِ وَحدَها.
2. **حِصَّةُ Groq المَجّانِيَّةُ لا تَحمِلُ دِراسَةَ جَدوى**: ‏**8000 رَمزٍ
   في الدَقيقَة و200,000 في اليَوم** مَعَ ‏30 طَلَباً/د و1000/يَوم
   ([المَنشور](https://console.groq.com/docs/rate-limits)). دِراسَةٌ واحِدَةٌ
   طَويلَةٌ تَبتَلِعُ الدَقيقَةَ كامِلَةً.
3. **نَماذِجُ OpenRouter `:free` بِسَقفٍ صُلب**: ‏20 طَلَباً/د، و**‏50
   طَلَباً في اليَوم** دونَ عَشَرَةِ أَرصِدَةٍ مُشتَراة، و**‏1000/يَوم**
   بَعدَها — سَقفٌ لا يَحمِلُ ظَهراً إنتاجِيّاً.

**والخُلاصَةُ العَمَلِيَّة**: الطَبَقاتُ المَجّانِيَّةُ لِلتَطويرِ
والتَجرِبَة، **والإنتاجُ يُشتَرى**. وأَرخَصُ مَدفوعٍ كافٍ لِتَحليلٍ نَصِّيٍّ
طَويلٍ هُوَ `gemini-2.5-flash-lite` (‏0.10 / 0.40، وسِياقٌ مَليون) أَو
`deepseek-v4-flash` إن تَكَرَّرَتِ الوَثائِقُ فَأَصابَ الكاش.

> **وتَنبيهٌ على طَبَقَةِ التَوافُقِ لَدى Anthropic**: لَها عُنوانٌ
> مُتَوافِقٌ مَعَ OpenAI، **ولا تُستَعمَل** — تَوثيقُها يَقولُ إنَّها «‏not
> considered a long-term or production-ready solution»، **وتُعَطِّلُ
> `prompt caching`** الَّذي تَقومُ عَلَيهِ `AnthropicBackend` هُنا. فَمَعَ
> Anthropic يُضبَطُ `Provider=anthropic` ويُترَكُ `BaseUrl` فارِغاً.

> **و`anthropic` هُوَ الفَرعُ الافتِراضيّ**: `AgentBackendFactory.Create`
> يُطابِقُ `"gemini"` و`"openai"` فَقَط، **وكُلُّ ما عَداهُما — بِما فيهِ
> الخَطَأُ المَطبَعيّ — يَقَعُ على Anthropic**. فَقيمَةٌ مِثلُ `"openrouter"`
> في `Agent__Provider` لا تُخطِئُ بِصَوت، بَل تُنادي `api.anthropic.com`
> بِمِفتاحٍ غَريب. وسَطرُ §٥ يُظهِرُ ذلك فَوراً.
