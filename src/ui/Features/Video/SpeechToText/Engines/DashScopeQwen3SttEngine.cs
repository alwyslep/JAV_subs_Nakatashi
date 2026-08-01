using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform;
using Nikse.SubtitleEdit.UiLogic.AudioToText;
using Nikse.SubtitleEdit.Features.Video.SpeechToText.DashScope;
using Nikse.SubtitleEdit.Logic.Config;

namespace Nikse.SubtitleEdit.Features.Video.SpeechToText.Engines;

/// <summary>
/// Online speech-to-text via Alibaba Cloud Model Studio (DashScope) Qwen3-ASR.
/// Uses the asynchronous file-transcription path (upload → submit → poll →
/// fetch) so real sentence timestamps are returned. See
/// <see cref="DashScopeSttService"/> for the request flow.
/// </summary>
public class DashScopeQwen3SttEngine : IOnlineSttEngine
{
    public static string StaticName => "Alibaba Qwen3-ASR";
    public string Name => StaticName;
    public string Choice => WhisperChoice.DashScopeQwen3;
    public string Url => "https://www.alibabacloud.com/help/en/model-studio/qwen-speech-recognition";

    public override string ToString() => Name;

    public List<WhisperLanguage> Languages => WhisperLanguage.Languages.OrderBy(p => p.Name).ToList();
    public List<WhisperModel> Models => new();

    public string Extension => string.Empty;
    public string UnpackSkipFolder => string.Empty;

    public bool IsEngineInstalled() => true;
    public bool CanBeDownloaded() => false;

    /// <summary>
    /// Reads the app's settings into the service's own settings object.
    /// ★Lives here rather than on <see cref="DashScopeSttService"/> so that service depends on no
    ///   app configuration at all - which is what lets it be compiled into a standalone
    ///   transcription tool by linking the source instead of copying it. A copy would fork the OSS
    ///   upload rules, and those took a live four-way probe to establish.
    /// </summary>
    public static DashScopeSttSettings GetSettingsFromConfiguration()
    {
        var tools = Se.Settings.Tools;
        return new DashScopeSttSettings
        {
            ApiKey = tools.DashScopeSttApiKey,
            Model = tools.DashScopeSttModel,
            Language = tools.DashScopeSttLanguage,
            Region = tools.DashScopeSttRegion,
            EnableWords = tools.DashScopeSttEnableWords,
            TimeoutSeconds = tools.DashScopeSttTimeoutSeconds,
            VocabularyId = tools.DashScopeSttVocabularyId,
            SpeakerCount = tools.DashScopeSttSpeakerCount,
            Logger = Se.WriteToolsLog,
        };
    }

    public ISttTranscriber? CreateTranscriber(out string? configErrorMessage)
    {
        var settings = GetSettingsFromConfiguration();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            configErrorMessage = Se.Language.General.OnlineSttApiKeyMissing;
            return null;
        }

        configErrorMessage = null;
        return new DashScopeSttService(settings);
    }

    public string ProbeUrl => DashScopeSttService.GetBaseUrl(Se.Settings.Tools.DashScopeSttRegion);

    // The async Filetrans job accepts the whole file (up to 12 h) via a single
    // upload, so never split it into chunks — set the threshold beyond any file.
    public long UploadThresholdBytes => long.MaxValue;
    public long ChunkSizeBytes => long.MaxValue;

    public string GetAndCreateWhisperFolder() => WhisperHelper.GetWhisperFolder(WhisperChoice.DashScopeQwen3);
    public string GetAndCreateWhisperModelFolder(WhisperModel? whisperModel) => new WhisperModel().ModelFolder;
    public string GetExecutable() => string.Empty;
    public bool IsModelInstalled(WhisperModel model) => true;
    public string GetModelForCmdLine(string modelName) => modelName;
    public string GetWhisperModelDownloadFileName(WhisperModel whisperModel, string url) => string.Empty;

    public async Task<string> GetHelpText()
    {
        var uri = new Uri("avares://SubtitleEdit/Assets/SpeechToText/AlibabaQwen3Asr.txt");
        try
        {
            await using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch
        {
            return "Alibaba Cloud (DashScope) Qwen3-ASR speech-to-text.\n\n" +
                   "Create an API key in Alibaba Cloud Model Studio, pick your region, and set the key.\n" +
                   "The audio is uploaded to DashScope temporary storage and transcribed asynchronously " +
                   "with sentence timestamps.";
        }
    }

    public string CommandLineParameter
    {
        get => string.Empty;
        set { }
    }
}
