using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;


namespace returngis.function
{
    public static class WavToText
    {
        [FunctionName("WavToText")]
        public async static void Run([BlobTrigger("wavs/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob, string name, ILogger log)
        {
            log.LogInformation($"WavToText processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");

            var config = SpeechConfig.FromSubscription(Environment.GetEnvironmentVariable("SpeechKey"), Environment.GetEnvironmentVariable("SpeechRegion"));
            var stopRecognition = new TaskCompletionSource<int>();

            var temp = string.Format("{0}.wav", Path.GetTempFileName());

            using (var ms = new MemoryStream())
            {
                myBlob.CopyTo(ms);
                File.WriteAllBytes(temp, ms.ToArray());
            }

            log.LogInformation($"Temp file {temp} created.");

            using (var audioInput = AudioConfig.FromWavFileInput(temp))
            {
                using (var recognizer = new SpeechRecognizer(config, audioInput))
                {

                    var transcript = new StringBuilder();

                    // Subscribes to events.
                    recognizer.Recognizing += (s, e) =>
                    {
                        log.LogInformation($"RECOGNIZING: Text={e.Result.Text}");
                    };

                    recognizer.Recognized += (s, e) =>
                    {
                        if (e.Result.Reason == ResultReason.RecognizedSpeech)
                        {
                            log.LogInformation($"RECOGNIZED: Text={e.Result.Text}");
                            transcript.Append($" {e.Result.Text} ");
                        }
                        else if (e.Result.Reason == ResultReason.NoMatch)
                        {
                            log.LogInformation($"NOMATCH: Speech could not be recognized.");
                        }
                    };

                    recognizer.Canceled += (s, e) =>
                    {
                        log.LogInformation($"CANCELED: Reason={e.Reason}");

                        if (e.Reason == CancellationReason.Error)
                        {
                            log.LogInformation($"CANCELED: ErrorCode={e.ErrorCode}");
                            log.LogInformation($"CANCELED: ErrorDetails={e.ErrorDetails}");
                            log.LogInformation($"CANCELED: Did you update the subscription info?");
                        }

                        stopRecognition.TrySetResult(0);
                    };

                    recognizer.SessionStarted += (s, e) =>
                    {
                        log.LogInformation("\n    Session started event.");
                    };

                    recognizer.SessionStopped += (s, e) =>
                    {
                        log.LogInformation("\n    Session stopped event.");
                        log.LogInformation("\nStop recognition.");
                        log.LogInformation("\nTranscript: ");
                        log.LogInformation(transcript.ToString());
                        stopRecognition.TrySetResult(0);
                    };

                    // Starts continuous recognition. Uses StopContinuousRecognitionAsync() to stop recognition.
                    await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);

                    Task.WaitAny(new[] { stopRecognition.Task });

                    // Stops recognition.
                    await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);

                    //save the file in the transcripts container                    
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
                    
                    var client = storageAccount.CreateCloudBlobClient();
                    var container = client.GetContainerReference("transcripts");
                    await container.CreateIfNotExistsAsync();

                    var blob = container.GetBlockBlobReference(name.Replace("wav", "txt"));
                    var transcriptTempFile = string.Format("{0}.txt", Path.GetTempFileName());
                    File.WriteAllText(transcriptTempFile, transcript.ToString());
                    await blob.UploadFromFileAsync(transcriptTempFile);

                    File.Delete(transcriptTempFile);
                    File.Delete(temp);
                }
            }
        }
    }
}