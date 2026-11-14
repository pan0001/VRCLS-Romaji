using System.Collections.Concurrent;
using System.Reflection;
using System.Net;
using System.Net.Http;
using VRCLS.Core.Models;
using VRCLS.Romaji;
using VRCLS.Services.Recognition;

internal static class MicOriginalChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Original = "我想直接发送这句中文。";
    private const string Translated = "テスト訳";

    public static async Task Run()
    {
        // Set only the field in this test process. Never save a preference on the user's machine.
        var enabled = typeof(DesktopRomaji).Assembly.GetType("VRCLS.Romaji.MicOriginalSettings", true)
            .GetField("enabled", BindingFlags.NonPublic | BindingFlags.Static);
        bool saved = (bool)enabled.GetValue(null);
        try
        {
            enabled.SetValue(null, false);
            await Verify("Mic", expectTranslation: true, expectChat: "[" + Translated + "]");
            enabled.SetValue(null, true);
            await Verify("Mic", expectTranslation: false, expectChat: Original);
            await Verify("Mic", expectTranslation: false, expectChat: Original, pretranslated: true);
            await Verify("Desktop", expectTranslation: true, expectChat: null);
            enabled.SetValue(null, false);
            await Verify("Mic", expectTranslation: true, expectChat: "[" + Translated + "]");
        }
        finally { enabled.SetValue(null, saved); }
    }

    private static async Task Verify(string source, bool expectTranslation, string expectChat, bool pretranslated = false)
    {
        var config = new AppConfig
        {
            MicSourceLanguage = "zh", MicTargetTranslationLanguage = "ja", DesktopTargetTranslationLanguage = "en",
            VrcChatboxFormatNew = "{translatedText}", VrcChatboxFormatText = "模板前缀{text}",
            ChatboxOscMixTemplate = "[{clientdata}]", TextInSteamVr = false,
            ControlEnabled = false, TtsToggle = 0, OscShutdown = false
        };
        var messages = new ConcurrentQueue<string>();
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int translations = 0;
        string path = null;
        Dictionary<string, string> languages = new();
        object Respond(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_State": return new BackendAuthState();
                case "SendAuthorizedAsync":
                    // Inspect the production multipart request without invoking its network send delegate.
                    using (var request = ((Func<HttpRequestMessage>)args[1])())
                    {
                        path = request.RequestUri.AbsolutePath;
                        foreach (var part in (MultipartFormDataContent)request.Content)
                        {
                            var name = part.Headers.ContentDisposition.Name.Trim('"');
                            if (name.Contains("Language"))
                                languages[name] = part.ReadAsStringAsync().GetAwaiter().GetResult();
                        }
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
                case "TranslateAsync":
                    translations++;
                    return Task.FromResult(Translated);
                case "SendChatbox":
                    messages.Enqueue((string)args[0]);
                    sent.TrySetResult();
                    return null;
                default:
                    if (method.Name.StartsWith("add_") || method.Name.StartsWith("remove_")) return null;
                    throw new InvalidOperationException("Unexpected service use in offline check: " + method.Name);
            }
        }
        object Service(Type type)
        {
            if (type == typeof(AppConfig)) return config;
            if (!type.IsInterface) return Activator.CreateInstance(type);
            var proxy = DispatchProxy.Create(type, typeof(OfflineServiceProxy));
            ((OfflineServiceProxy)proxy).Respond = Respond;
            return proxy;
        }

        var auth = (VRCLS.Core.Interfaces.IBackendAuthService)Service(typeof(VRCLS.Core.Interfaces.IBackendAuthService));
        using var recognition = new FastApiRecognitionService(config, source, auth);
        var recognize = recognition.GetType().GetMethod("SendAudioAsync", PrivateInstance);
        await (Task)recognize.Invoke(recognition, new object[] { new float[1600], null, CancellationToken.None });
        var endpoint = expectTranslation ? "/func/multitranslateToOtherLanguage" : "/whisper/multitranscription";
        Check(path?.EndsWith(endpoint, StringComparison.Ordinal) == true, "Recognition endpoint");
        var targets = languages.Where(pair => pair.Key.StartsWith("targetLanguage")).Select(pair => pair.Value);
        var wantedTargets = expectTranslation ? config.ResolveEnabledTargetLanguages(source) : Array.Empty<string>();
        Check(targets.SequenceEqual(wantedTargets) && (!expectTranslation || wantedTargets.Count > 0), "ASR request targets");
        if (source == "Mic") Check(languages["sourceLanguage"] == "zh", "ASR source language preserved");
        var type = Assembly.Load("VRCLS").GetType("VRCLS.UI.ViewModels.MainViewModel", true);
        var ctor = type.GetConstructors().Single();
        var vm = ctor.Invoke(ctor.GetParameters().Select(p => Service(p.ParameterType)).ToArray());
        // Simulate active capture without bringing up endpoints, audio devices or OSC sockets.
        type.GetField("_isMicListening", PrivateInstance).SetValue(vm, true);
        type.GetField("_isDesktopListening", PrivateInstance).SetValue(vm, true);
        var handler = type.GetMethod("HandleRecognitionResultAsync", PrivateInstance);
        var result = new TranscriptionResult { Original = Original, Source = source, IsFinal = false };
        await (Task)handler.Invoke(vm, new object[] { result });
        Check(translations == 0 && messages.IsEmpty && type.GetField("_oscSendCts", PrivateInstance).GetValue(vm) == null, "Partial result did not translate/send");
        result.IsFinal = true;
        if (pretranslated) result.Translation = result.Translation2 = result.Translation3 = "旧译文";
        await (Task)handler.Invoke(vm, new object[] { result });
        if (expectChat != null) await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        else Check(type.GetField("_oscSendCts", PrivateInstance).GetValue(vm) == null, "No desktop OSC send scheduled");
        Check(translations == (expectTranslation ? 1 : 0), "Fallback translator call count");
        Check(messages.SequenceEqual(expectChat == null ? Array.Empty<string>() : new[] { expectChat }), "Exact OSC chatbox payload");
        if (!expectTranslation)
            Check(string.IsNullOrEmpty(result.Translation) && string.IsNullOrEmpty(result.Translation2) &&
                string.IsNullOrEmpty(result.Translation3), "Preexisting translations cleared");
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException($"{label} failed for {source}, originalOnly={!expectTranslation}");
        }
    }
}

public class OfflineServiceProxy : DispatchProxy
{
    public Func<MethodInfo, object[], object> Respond { get; set; }
    protected override object Invoke(MethodInfo targetMethod, object[] args) => Respond(targetMethod, args);
}
