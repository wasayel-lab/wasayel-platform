# بَدائِل LLM مَجّانيَّة فِعليَّة

> **تَصحيحٌ مَقيس (‏2026-08-31)** — أَربَعَةُ أَرقامٍ في هذِه الصَفحَةِ
> تَقادَمَت، وواحِدٌ مِنها كانَ يَقودُ إلى قَرارٍ خاطِئ:
> **١)** حِصَّةُ Groq المَجّانِيَّةُ لَيسَت «‏30/دَقيقَة و14,400/يَوم» —
> المَنشورُ اليَومَ **‏30 طَلَباً/د · 1000/يَوم · 8000 رَمزٍ/د · 200,000
> رَمزٍ/يَوم**، والسَقفُ الرَمزِيُّ هُوَ المُلزِمُ لا الطَلَبِيّ: دِراسَةُ
> جَدوى واحِدَةٌ طَويلَةٌ تَبتَلِعُ الدَقيقَةَ كامِلَةً
> ([المَصدَر](https://console.groq.com/docs/rate-limits)).
> **٢)** ونَماذِجُ Llama و Mixtral أَدناهُ لَم تَعُد في قائِمَةِ Groq
> المَجّانِيَّة؛ المَنشورُ مَجّاناً اليَومَ `openai/gpt-oss-120b` و
> `gpt-oss-20b` و`qwen/qwen3.6-27b`.
> **٣)** و**‏GitHub Models أُغلِقَ نِهائِيّاً في ‏2026-07-30** فَلا يُبنى
> عَلَيه (‏[`AGENT-KEYS.md`](AGENT-KEYS.md) §٣).
> **٤)** والطَبَقاتُ المَجّانِيَّةُ لا تَصلُحُ ظَهراً إنتاجِيّاً — القُيودُ
> الثَلاثَةُ بِنَصِّها في [`AGENT-KEYS.md`](AGENT-KEYS.md) §٧.
>
> **وتَشخيصُ ‏401/404/429 وأَسماءُ المُتَغَيِّراتِ الأَربَعَةِ بِحَرفِها
> في [`AGENT-KEYS.md`](AGENT-KEYS.md).**

نَفس `OpenAIBackend` يَعمَل مَع أَيّ مُزَوِّد يَتَكَلَّم Chat Completions API.
بَدِّل `Agent:BaseUrl` و `Agent:ApiKey` و `Agent:Model` في `appsettings.Local.json`.

> **مُنذ 2026-08-10**: هذِه المَفاتيح صارَت **مُشتَرَكَة بَين الوُكَلاء** —
> يُمكِن تَخصيص كُلّ وَكيل عَلى حِدَة بِـ`Agents:{Studio|Analysis}:{Key}`،
> وما لا يُذكَر هُناك يَسقُط إلى `Agent:*` أَدناه بِلا تَغيير. القاعِدَة
> كامِلَةً ومِثال GitHub Models الثُنائيّ في
> [AGENT-TOOLS §1](AGENT-TOOLS.md).

## ١) Groq — الأَسرَع، أَجوَد حِصَّة مَجّانيَّة

**التَّسجيل**: console.groq.com → API Keys → "Create API Key" (مَجّاناً).
**الحِصَّة**: ~30 requests/دَقيقَة + ~14,400 يَوميّاً (تَكفي مَئات تَحاليل/يَوم).

```json
{
  "Agent": {
    "Provider": "openai",
    "BaseUrl": "https://api.groq.com/openai/",
    "ApiKey":  "gsk_…",
    "Model":   "llama-3.3-70b-versatile"
  }
}
```

نَماذِج جَيِّدَة عَلى Groq لِلتَحليل العَرَبي:
- `llama-3.3-70b-versatile` — جَيِّد لِلجَودَة
- `llama-3.1-70b-versatile` — مَوثوق
- `mixtral-8x7b-32768` — أَسرَع، أَقَلّ جَودَة

## ٢) Cerebras — مُشابِه Groq، سُرعَة مُلفِتَة

```json
{
  "Agent": {
    "Provider": "openai",
    "BaseUrl": "https://api.cerebras.ai/",
    "ApiKey":  "csk-…",
    "Model":   "llama3.3-70b"
  }
}
```

## ٣) OpenRouter — نَماذِج "مَجّانيَّة" مُتَعَدِّدَة في واجِهَة واحِدَة

التَّسجيل عَلى openrouter.ai، احصُل عَلى مِفتاح. النَّماذِج بِلاحِقَة `:free` بِلا تَكلِفَة (مَع حِصَّة).

```json
{
  "Agent": {
    "Provider": "openai",
    "BaseUrl": "https://openrouter.ai/api/",
    "ApiKey":  "sk-or-…",
    "Model":   "deepseek/deepseek-chat-v3:free"
  }
}
```

نَماذِج `:free` مُفيدَة:
- `deepseek/deepseek-chat-v3:free` — قَويّ لِلتَحليل
- `meta-llama/llama-3.3-70b-instruct:free`
- `google/gemini-2.0-flash-exp:free`
- `qwen/qwen-2.5-72b-instruct:free`

## ٤) Ollama المَحَلّي — مَجّاني تَماماً، بِلا حُدود

شَغِّل LLM عَلى جِهازَك. يَحتاج تَحميل (~4-40 GB حَسَب النَّموذَج):

```bash
# تَثبيت
curl -fsSL https://ollama.com/install.sh | sh
# تَحميل نَموذَج
ollama pull qwen2.5:14b        # ~9 GB، جَيِّد لِلعَرَبي
# أَو:
ollama pull llama3.3:70b       # ~40 GB، الأَفضَل (يَحتاج GPU)
# تَشغيل (يَستَمِع عَلى 11434)
ollama serve
```

```json
{
  "Agent": {
    "Provider": "openai",
    "BaseUrl": "http://localhost:11434/",
    "ApiKey":  "",
    "Model":   "qwen2.5:14b"
  }
}
```

(`ApiKey` يُتَجاهَل في Ollama، الـ backend يَعتَبِره مُكَوَّناً بِدونه.)

## ٥) DeepSeek مُباشَرَةً — رَخيص جِدّاً (لَيس مَجّانيّاً لكِنّ شِبه ذلِك)

~$0.14 لِكُلّ مَليون token دَخل. تَحليل واحِد كامِل ≈ سِنت أَو سِنتَين.

```json
{
  "Agent": {
    "Provider": "openai",
    "BaseUrl": "https://api.deepseek.com/",
    "ApiKey":  "sk-…",
    "Model":   "deepseek-chat"
  }
}
```

## التَّوصِيَة العَمَلِيَّة

- **اِبدَأ بِـ Groq** + `llama-3.3-70b-versatile` — حِصَّة كَبيرَة، سُرعَة عالِيَة، مِفتاح في دَقيقَة.
- لَو احتَجت جَودَة أَعلى ولا حَدّ: **Ollama + qwen2.5:14b** عَلى جِهازَك.
- لَو تُريد تَجريب عِدَّة نَماذِج بِواجِهَة واحِدَة: **OpenRouter** + نَموذَج `:free`.

كُلّ التَّبديل بَين هؤلاء = ٤ سُطور JSON + إعادَة تَشغيل التَطبيق.
