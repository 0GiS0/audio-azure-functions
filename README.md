# Audio utilities using Azure Functions

Conversions using ffmpeg and transcriptions with Speech Service

* **Mp3ToWav**: It converts from MP3 to WAV in order to be treated by Speech service from Cognitive Services, but it can be used simply as a transformation method.
* **WavToText**: Use Speech Service to get the audio transcript.

## Quick Deploy to Azure

[![Deploy to Azure](http://azuredeploy.net/deploybutton.svg)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2F0GiS0%2Faudio-azure-functions%2Fmaster%2Fazure.deploy.json)

## More Information

I wrote a post explaining the code on my blog [return(GiS);](https://www.returngis.net/2018/11/azure-functions-para-procesar-mp3s-con-speech-service/)
