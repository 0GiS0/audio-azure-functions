using System;
using System.IO;
using System.Diagnostics;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Core.Util;
using System.Threading;
using System.Threading.Tasks;

namespace returngis.function
{
    public static class Mp3ToWav
    {
        static long totalBytes = 0;
        static ILogger trace;

        [FunctionName("Mp3ToWav")]
        public async static void Run([BlobTrigger("mp3s/{name}", Connection = "AzureWebJobsStorage")]Stream myBlob, string name, ILogger log)
        {
            trace = log;
            trace.LogInformation($"Mp3ToWav function processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");

            var mp3Temp = string.Format("{0}.mp3", Path.GetTempFileName());
            var wavTemp = string.Format("{0}.wav", Path.GetTempFileName());

            using (var ms = new MemoryStream())
            {
                myBlob.CopyTo(ms);
                File.WriteAllBytes(mp3Temp, ms.ToArray());
            }

            var mp3Bytes = File.ReadAllBytes(mp3Temp);
            trace.LogInformation($"mp3 size: {ConvertBytesToMegabytes(mp3Bytes.Length).ToString("0.00")} MB");

            var process = new Process();

            process.StartInfo.FileName = Environment.GetEnvironmentVariable("ffmpegPath");
            process.StartInfo.Arguments = $"-i \"{mp3Temp}\" \"{wavTemp}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;

            process.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                trace.LogInformation($"O: {e.Data}");
            });

            process.ErrorDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                trace.LogInformation($"E: {e.Data}");
            });

            process.Start();
            trace.LogInformation("***Converting from mp3 to wav***");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            trace.LogInformation($"ffmpeg exit code: {process.ExitCode}");
            trace.LogInformation($"wav temp out exists: {File.Exists(wavTemp)}");

            var bytes = File.ReadAllBytes(wavTemp);
            trace.LogInformation($"wav size: {ConvertBytesToMegabytes(bytes.Length).ToString("0.00")} MB");

            trace.LogInformation("***Uploading wav to Azure Storage***");

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("AzureWebJobsStorage"));
            var client = storageAccount.CreateCloudBlobClient();
            var container = client.GetContainerReference("wavs");
            await container.CreateIfNotExistsAsync();

            var blob = container.GetBlockBlobReference(name.Replace("mp3", "wav"));
            blob.Properties.ContentType = "audio/wav";

            var progressHandler = new Progress<StorageProgress>();
            progressHandler.ProgressChanged += ProgressHandler_ProgressChanged;

            using (Stream stream = new MemoryStream(bytes))
            {
                totalBytes = File.Open(wavTemp, FileMode.Open).Length;

                try
                {
                    await blob.UploadFromStreamAsync(stream, null, new BlobRequestOptions(), new OperationContext(), progressHandler, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    trace.LogInformation($"Error: {ex.Message}");
                }
            }


            //Delete temp files
            File.Delete(mp3Temp);
            File.Delete(wavTemp);

            trace.LogInformation("Done!");
        }

        static double ConvertBytesToMegabytes(long bytes)
        {
            return (bytes / 1024f) / 1024f;
        }
        private static void ProgressHandler_ProgressChanged(object sender, StorageProgress e)
        {
            double dProgress = ((double)e.BytesTransferred / totalBytes) * 100.0;

            trace.LogInformation($"{dProgress.ToString("0.00")}% bytes transferred: { ConvertBytesToMegabytes(e.BytesTransferred).ToString("0.00")} MB from {ConvertBytesToMegabytes(totalBytes).ToString("0.00")} MB");
        }
    }
}