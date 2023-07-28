//
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE.md file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Translation;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Collections.Generic;

namespace Azure.AI.Details.Common.CLI
{
    public class TranslateCommand : Command
    {
        internal TranslateCommand(ICommandValues values)
        {
            _values = values.ReplaceValues();
        }

        internal bool RunCommand()
        {
            Recognize(_values["recognize.method"]);
            return _values.GetOrDefault("passed", true);
        }

        private void Recognize(string recognize)
        {
            switch (recognize)
            {
                case "":
                case null:
                case "continuous": RecognizeContinuous(); break;

                case "once": RecognizeOnce(false); break;
                case "once+": RecognizeOnce(true); break;

                case "keyword": RecognizeKeyword(); break;

                case "rest": RecognizeREST(); break;
            }
        }

        private void RecognizeREST()
        {
            _values.AddThrowError("WARNING:", $"'recognize=rest' NOT YET IMPLEMENTED!!");
        }

        private void RecognizeContinuous()
        {
            StartCommand();

            TranslationRecognizer recognizer = CreateTranslationRecognizer();
            PrepRecognizerConnection(recognizer, true);
            PrepRecognizerGrammars(recognizer);

            recognizer.SessionStarted += SessionStarted;
            recognizer.SessionStopped += SessionStopped;
            recognizer.Recognizing += Recognizing;
            recognizer.Recognized += Recognized;
            recognizer.Canceled += Canceled;

            recognizer.StartContinuousRecognitionAsync().Wait();
            if (_microphone) { Console.WriteLine("Listening; press ENTER to stop ...\n"); }

            var timeout = _values.GetOrDefault("recognize.timeout", int.MaxValue);
            WaitForContinuousStopCancelKeyOrTimeout(recognizer, timeout);

            recognizer.StopContinuousRecognitionAsync().Wait();

            if (_disconnect) RecognizerConnectionDisconnect(recognizer);

            StopCommand();
            DisposeAfterStop();
            DeleteTemporaryFiles();
        }

        private void RecognizeOnce(bool repeatedly)
        {
            StartCommand();

            TranslationRecognizer recognizer = CreateTranslationRecognizer();
            PrepRecognizerConnection(recognizer, false);
            PrepRecognizerGrammars(recognizer);

            recognizer.SessionStarted += SessionStarted;
            recognizer.SessionStopped += SessionStopped;
            recognizer.Recognizing += Recognizing;
            recognizer.Recognized += Recognized;
            recognizer.Canceled += Canceled;

            if (_microphone) { Console.WriteLine("Listening..."); }

            while (true)
            {
                var task = recognizer.RecognizeOnceAsync();
                WaitForOnceStopCancelOrKey(recognizer, task);

                if (_disconnect) RecognizerConnectionDisconnect(recognizer);

                if (!repeatedly) break;
                if (_canceledEvent.WaitOne(0)) break;
                if (_microphone && Console.KeyAvailable) break;
                if (_microphone && task.Result.Text == "Stop.") break;
                if (_microphone && task.Result.Text == "Stop listening.") break;
            }

            StopCommand();
            DisposeAfterStop();
            DeleteTemporaryFiles();
        }

        private void RecognizeKeyword()
        {
            StartCommand();

            TranslationRecognizer recognizer = CreateTranslationRecognizer();
            PrepRecognizerConnection(recognizer, false);
            PrepRecognizerGrammars(recognizer);

            recognizer.SessionStarted += SessionStarted;
            recognizer.SessionStopped += SessionStopped;
            recognizer.Recognizing += Recognizing;
            recognizer.Recognized += Recognized;
            recognizer.Canceled += Canceled;

            var keywordModel = LoadKeywordModel();
            recognizer.StartKeywordRecognitionAsync(keywordModel).Wait();
            if (_microphone) { Console.WriteLine("Listening for keyword..."); }

            var timeout = _values.GetOrDefault("recognize.timeout", int.MaxValue);
            WaitForKeywordCancelKeyOrTimeout(recognizer, timeout);

            recognizer.StopKeywordRecognitionAsync().Wait();

            if (_disconnect) RecognizerConnectionDisconnect(recognizer);

            StopCommand();
            DisposeAfterStop();
            DeleteTemporaryFiles();
        }

        private TranslationRecognizer CreateTranslationRecognizer()
        {
            SpeechTranslationConfig config = CreateSpeechTranslationConfig();
            AudioConfig audioConfig = ConfigHelpers.CreateAudioConfig(_values);

            SourceLanguageConfig language = null;
            AutoDetectSourceLanguageConfig autoDetect = null;
            CreateSourceLanguageConfig(config, out language, out autoDetect);

            // var recognizer = autoDetect != null 
            //     ? new TranslationRecognizer(config, autoDetect, audioConfig)
            //     : language != null
            //         ? new TranslationRecognizer(config, language, audioConfig)
            //         : audioConfig != null
            //             ? new TranslationRecognizer(config, audioConfig)
            //             : new TranslationRecognizer(config);

            var recognizer = audioConfig != null
                ? new TranslationRecognizer(config, audioConfig)
                : new TranslationRecognizer(config);

            _disposeAfterStop.Add(audioConfig);
            _disposeAfterStop.Add(recognizer);

            _output.EnsureCachePropertyCollection("recognizer", recognizer.Properties);

            return recognizer;
        }

        private void CreateSourceLanguageConfig(SpeechConfig config, out SourceLanguageConfig language, out AutoDetectSourceLanguageConfig autoDetect)
        {
            language = null;
            autoDetect = null;

            var bcp47 = _values["source.language.config"];
            if (string.IsNullOrEmpty(bcp47)) bcp47 = "en-US";

            var endpointId = _values["service.config.endpoint.id"];
            var endpointIdOk = !string.IsNullOrEmpty(endpointId);

            // once TranslationRecognizer supports source language config, remove this block
            // ... and uncomment associated lines from  CreateTranslationRecognizer, replacing existing new TranslationRecognizer* lines
            if (!string.IsNullOrEmpty(bcp47)) config.SpeechRecognitionLanguage = bcp47;

            var langs = new List<SourceLanguageConfig>();
            foreach (var item in bcp47.Split(';'))
            {
                langs.Add(endpointIdOk
                    ? SourceLanguageConfig.FromLanguage(item, endpointId)
                    : SourceLanguageConfig.FromLanguage(item));
            }

            if (langs.Count == 1)
            {
                language = langs[0];
            }
            else
            {
                autoDetect = AutoDetectSourceLanguageConfig.FromSourceLanguageConfigs(langs.ToArray());
            }
        }

        private SpeechTranslationConfig CreateSpeechTranslationConfig()
        {
            var key = _values["service.config.key"];
            var host = _values["service.config.host"];
            var region = _values["service.config.region"];
            var endpoint = _values["service.config.endpoint.uri"];
            var tokenValue = _values["service.config.token.value"];

            if (string.IsNullOrEmpty(endpoint) && string.IsNullOrEmpty(region) && string.IsNullOrEmpty(host))
            {
                _values.AddThrowError("ERROR:", $"Creating SpeechConfig; requires one of: region, endpoint, or host.");
            }
            else if (!string.IsNullOrEmpty(region) && string.IsNullOrEmpty(tokenValue) && string.IsNullOrEmpty(key))
            {
                _values.AddThrowError("ERROR:", $"Creating SpeechConfig; use of region requires one of: key or token.");
            }

            SpeechTranslationConfig config = null;
            if (!string.IsNullOrEmpty(endpoint))
            {
                config = string.IsNullOrEmpty(key)
                    ? SpeechTranslationConfig.FromEndpoint(new Uri(endpoint))
                    : SpeechTranslationConfig.FromEndpoint(new Uri(endpoint), key);
            }
            else if (!string.IsNullOrEmpty(host))
            {
                config = string.IsNullOrEmpty(key)
                    ? SpeechTranslationConfig.FromHost(new Uri(host))
                    : SpeechTranslationConfig.FromHost(new Uri(host), key);
            }
            else // if (!string.IsNullOrEmpty(region))
            {
                config = string.IsNullOrEmpty(tokenValue)
                    ? SpeechTranslationConfig.FromSubscription(key, region)
                    : SpeechTranslationConfig.FromAuthorizationToken(tokenValue, region);
            }

            if (!string.IsNullOrEmpty(tokenValue))
            {
                config.AuthorizationToken = tokenValue;
            }

            SetTranslationConfigProperties(config);
            return config;
        }

        private void SetTranslationConfigProperties(SpeechTranslationConfig config)
        {
            ConfigHelpers.SetupLogFile(config, _values);

            var target = _values.GetOrDefault("target.language.config", "en-US");
            if (!string.IsNullOrEmpty(target))
                foreach (var lang in target.Split(';'))
                    config.AddTargetLanguage(lang);

            var proxyHost = _values["service.config.proxy.host"];
            if (!string.IsNullOrEmpty(proxyHost)) config.SetProxy(proxyHost, _values.GetOrDefault("service.config.proxy.port", 80));

            var endpointId = _values["service.config.endpoint.id"];
            if (!string.IsNullOrEmpty(endpointId)) config.EndpointId = endpointId;

            var categoryId = _values["service.config.category.id"];
            if (!string.IsNullOrEmpty(categoryId)) config.SetServiceProperty("categoryId", categoryId, ServicePropertyChannel.UriQueryParameter);

            var needDetailedText = _output.NeedsLexicalText() || _output.NeedsItnText();
            if (needDetailedText) config.OutputFormat = OutputFormat.Detailed;

            var profanity = _values["service.output.config.profanity.option"];
            if (profanity == "removed") config.SetProfanity(ProfanityOption.Removed);
            if (profanity == "masked") config.SetProfanity(ProfanityOption.Masked);
            if (profanity == "raw") config.SetProfanity(ProfanityOption.Raw);

            var wordTimings = _values.GetOrDefault("service.output.config.word.level.timing", false);
            if (wordTimings) config.RequestWordLevelTimestamps();

            var contentLogging = _values.GetOrDefault("service.config.content.logging.enabled", false);
            if (contentLogging) config.EnableAudioLogging();

            var trafficType = _values.GetOrDefault("service.config.endpoint.traffic.type", "spx");
            config.SetServiceProperty("traffictype", trafficType, ServicePropertyChannel.UriQueryParameter);

            var endpointParam = _values.GetOrDefault("service.config.endpoint.query.string", "");
            if (!string.IsNullOrEmpty(endpointParam)) ConfigHelpers.SetEndpointParams(config, endpointParam);

            var httpHeader = _values.GetOrDefault("service.config.endpoint.http.header", "");
            if (!string.IsNullOrEmpty(httpHeader)) SetHttpHeaderProperty(config, httpHeader);

            var rtf = _values.GetOrDefault("audio.input.real.time.factor", -1);
            if (rtf >= 0) config.SetProperty("SPEECH-AudioThrottleAsPercentageOfRealTime", rtf.ToString());

            var fastLane = _values.GetOrDefault("audio.input.fast.lane", rtf >= 0 ? 0 : -1);
            if (fastLane >= 0) config.SetProperty("SPEECH-TransmitLengthBeforThrottleMs", fastLane.ToString());

            var stringProperty = _values.GetOrDefault("config.string.property", "");
            if (!string.IsNullOrEmpty(stringProperty)) ConfigHelpers.SetStringProperty(config, stringProperty);

            var stringProperties = _values.GetOrDefault("config.string.properties", "");
            if (!string.IsNullOrEmpty(stringProperties)) ConfigHelpers.SetStringProperties(config, stringProperties);

            CheckNotYetImplementedConfigProperties();
        }

        private static void SetHttpHeaderProperty(SpeechConfig config, string httpHeader)
        {
            string name = "", value = "";
            if (StringHelpers.SplitNameValue(httpHeader, out name, out value)) config.SetServiceProperty(name, value, ServicePropertyChannel.HttpHeader);
        }

        private void CheckNotYetImplementedConfigProperties()
        {
            var notYetImplemented = 
                ";config.token.type;config.token.password;config.token.username" +
                ";recognizer.property";

            foreach (var key in notYetImplemented.Split(';'))
            {
                var value = _values[key];
                if (!string.IsNullOrEmpty(value))
                {
                    _values.AddThrowError("WARNING:", $"'{key}={value}' NOT YET IMPLEMENTED!!");
                }
            }
        }

        private void CheckAudioInput()
        {
            var id = _values["audio.input.id"];
            var device = _values["audio.input.microphone.device"];
            var input = _values["audio.input.type"];
            var file = _values["audio.input.file"];
            var url = "";

            if (!string.IsNullOrEmpty(file) && file.StartsWith("http"))
            {
                file = DownloadInputFile(url = file, "audio.input.file", "audio input");
            }

            if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(url))
            {
                id = GetIdFromInputUrl(url, "audio.input.id");
            }

            if (string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(file))
            {
                id = GetIdFromAudioInputFile(input, file);
            }

            if (string.IsNullOrEmpty(input) && !string.IsNullOrEmpty(id))
            {
                input = GetAudioInputFromId(id);
            }

            if (input == "file" && string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(id))
            {
                file = GetAudioInputFileFromId(id);
            }

            _microphone = (input == "microphone" || string.IsNullOrEmpty(input));
        }

        private string GetIdFromAudioInputFile(string input, string file)
        {
            string id;
            if (input == "microphone" || string.IsNullOrEmpty(input))
            {
                id = "microphone";
            }
            else if (input == "file" && !string.IsNullOrEmpty(file))
            {
                var existing = FileHelpers.DemandFindFileInDataPath(file, _values, "audio input");
                id = Path.GetFileNameWithoutExtension(existing);
            }
            else
            {
                id = "error";
            }

            _values.Add("audio.input.id", id);
            return id;
        }

        private string GetAudioInputFromId(string id)
        {
            string input;
            if (id == "microphone")
            {
                input = "microphone";
            }
            else if (FileHelpers.FileExistsInDataPath(id, _values) || FileHelpers.FileExistsInDataPath(id + ".wav", _values))
            {
                input = "file";
            }
            else if (_values.Contains("audio.input.id.url"))
            {
                input = "file";
            }
            else
            {
                _values.AddThrowError("ERROR:", $"Cannot find audio input file: \"{id}.wav\"");
                return null;
            }

            _values.Add("audio.input.type", input);
            return input;
        }

        private string GetAudioInputFileFromId(string id)
        {
            string file;
            var existing = FileHelpers.FindFileInDataPath(id, _values);
            if (existing == null) existing = FileHelpers.FindFileInDataPath(id + ".wav", _values);

            if (existing == null)
            {
                var url = _values["audio.input.id.url"];
                if (!string.IsNullOrEmpty(url))
                {
                    url = url.Replace("{id}", id);
                    existing = HttpHelpers.DownloadFileWithRetry(url);
                }
            }

            file = existing;
            _values.Add("audio.input.file", file);
            return file;
        }

        private KeywordRecognitionModel LoadKeywordModel()
        {
            var fileName = _values["recognize.keyword.file"];
            var existing = FileHelpers.DemandFindFileInDataPath(fileName, _values, "keyword model");

            var keywordModel = KeywordRecognitionModel.FromFile(existing);
            return keywordModel;
        }

        private void PrepRecognizerGrammars(TranslationRecognizer recognizer)
        {
            var phrases = _values["grammar.phrase.list"];
            if (!string.IsNullOrEmpty(phrases)) PrepRecognizerPhraseListGrammar(recognizer, phrases.Split('\r', '\n', ';'));

            var partialPhraseRecognitionFactor = _values["grammar.recognition.factor.phrase"];
            if (!string.IsNullOrEmpty(partialPhraseRecognitionFactor)) PrepGrammarList(recognizer, double.Parse(partialPhraseRecognitionFactor));
        }

        private void PrepGrammarList(TranslationRecognizer recognizer, double partialPhraseRecognitionFactor)
        {
            var grammarList = GrammarList.FromRecognizer(recognizer);
            grammarList.SetRecognitionFactor(partialPhraseRecognitionFactor, RecognitionFactorScope.PartialPhrase);
        }

        private void PrepRecognizerPhraseListGrammar(TranslationRecognizer recognizer, IEnumerable<string> phrases)
        {
            var grammar = PhraseListGrammar.FromRecognizer(recognizer);
            foreach (var phrase in phrases)
            {
                if (!string.IsNullOrEmpty(phrase))
                {
                    grammar.AddPhrase(phrase);
                }
            }
        }

        private void PrepRecognizerConnection(TranslationRecognizer recognizer, bool continuous)
        {
            var connection = Connection.FromRecognizer(recognizer);
            _disposeAfterStop.Add(connection);

            connection.Connected += Connected;
            connection.Disconnected += Disconnected;
            connection.MessageReceived += ConnectionMessageReceived;

            var connect = _values["connection.connect"];
            var disconnect = _values["connection.disconnect"];

            _connect = !string.IsNullOrEmpty(connect) && connect == "true";
            _disconnect = !string.IsNullOrEmpty(disconnect) && disconnect == "true";

            if (_connect) RecognizerConnectionConnect(connection, continuous);
            ConnectionHelpers.SetConnectionMessageProperties(connection, _values);
        }

        private void RecognizerConnectionConnect(Connection connection, bool continuous)
        {
            connection.Open(continuous);
        }

        private void RecognizerConnectionDisconnect(TranslationRecognizer recognizer)
        {
            _lock.EnterReaderLockOnce(ref _expectDisconnected);

            var connection = Connection.FromRecognizer(recognizer);
            connection.Close();
        }

        private void Connected(object sender, ConnectionEventArgs e)
        {
            _display.DisplayConnected(e);
            _output.Connected(e);
        }

        private void Disconnected(object sender, ConnectionEventArgs e)
        {
            _display.DisplayDisconnected(e);
            _output.Disconnected(e);

            _lock.ExitReaderLockOnce(ref _expectDisconnected);
        }

        private void ConnectionMessageReceived(object sender, ConnectionMessageEventArgs e)
        {
            _display.DisplayMessageReceived(e);
            _output.ConnectionMessageReceived(e);
        }

        private void SessionStarted(object sender, SessionEventArgs e)
        {
            _lock.EnterReaderLockOnce(ref _expectSessionStopped);
            _stopEvent.Reset();

            _display.DisplaySessionStarted(e);
            _output.SessionStarted(e);
        }

        private void SessionStopped(object sender, SessionEventArgs e)
        {
            _display.DisplaySessionStopped(e);
            _output.SessionStopped(e);

            _stopEvent.Set();
            _lock.ExitReaderLockOnce(ref _expectSessionStopped);
        }

        private void Recognizing(object sender, TranslationRecognitionEventArgs e)
        {
            _lock.EnterReaderLockOnce(ref _expectRecognized);

            _display.DisplayRecognizing(e);
            _output.Recognizing(e);
        }

        private void Recognized(object sender, TranslationRecognitionEventArgs e)
        {
            _display.DisplayRecognized(e);
            _output.Recognized(e);

            _lock.ExitReaderLockOnce(ref _expectRecognized);
        }

        private void Canceled(object sender, TranslationRecognitionCanceledEventArgs e)
        {
            _display.DisplayCanceled(e);
            _output.Canceled(e);
            _canceledEvent.Set();
        }

        private void WaitForContinuousStopCancelKeyOrTimeout(TranslationRecognizer recognizer, int timeout)
        {
            var interval = 100;

            while (timeout > 0)
            {
                timeout -= interval;
                if (_stopEvent.WaitOne(interval)) break;
                if (_canceledEvent.WaitOne(0)) break;
                if (_microphone && Console.KeyAvailable)
                {
                    recognizer.StopContinuousRecognitionAsync().Wait();
                    break;
                }
            }
        }

        private void WaitForOnceStopCancelOrKey(TranslationRecognizer recognizer, Task<TranslationRecognitionResult> task)
        {
            var interval = 100;

            while (!task.Wait(interval))
            {
                if (_stopEvent.WaitOne(0)) break;
                if (_canceledEvent.WaitOne(0)) break;
                if (_microphone && Console.KeyAvailable)
                {
                    recognizer.StopContinuousRecognitionAsync().Wait();
                    break;
                }
            }
        }

        private void WaitForKeywordCancelKeyOrTimeout(TranslationRecognizer recognizer, int timeout)
        {
            var interval = 100;

            while (timeout > 0)
            {
                timeout -= interval;
                if (_canceledEvent.WaitOne(interval)) break;
                if (_microphone && Console.KeyAvailable)
                {
                    recognizer.StopKeywordRecognitionAsync().Wait();
                    break;
                }
            }
        }

        private void StartCommand()
        {
            CheckPath();
            CheckAudioInput();

            _display = new DisplayHelper(_values);

            _output = new OutputHelper(_values);
            _output.StartOutput();

            var id = _values["audio.input.id"];
            _output.EnsureOutputAll("audio.input.id", id);
            _output.EnsureOutputEach("audio.input.id", id);
            _output.EnsureCacheProperty("audio.input.id", id);

            var file = _values["audio.input.file"];
            _output.EnsureCacheProperty("audio.input.file", file);

            _lock = new SpinLock();
            _lock.StartLock();

            _expectRecognized = 0;
            _expectSessionStopped = 0;
            _expectDisconnected = 0;
        }

        private void StopCommand()
        {
            _lock.StopLock(5000);

            _output.CheckOutput();
            _output.StopOutput();
        }

        private bool _microphone = false;
        private bool _connect = false;
        private bool _disconnect = false;

        private SpinLock _lock = null;
        private int _expectRecognized = 0;
        private int _expectSessionStopped = 0;
        private int _expectDisconnected = 0;

        OutputHelper _output = null;
        DisplayHelper _display = null;
    }
}
