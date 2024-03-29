using System;
using System.IO;
using System.Threading.Tasks;
using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using CreateThumbnailFunction.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Functions
{
    public class CreateThumbnailFunction
    {
        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task ExecuteAsync(S3Event evnt, ILambdaContext context)
        {
            try
            {
                var s3Event = evnt.Records?[0].S3;
                var getResponse = await GetS3Object(s3Event.Bucket.Name, s3Event.Object.Key);
                using (var responseStream = getResponse.ResponseStream)
                {
                    using (var resizedStream = GetResizedStream(responseStream, 0.2m, getResponse.Headers.ContentType))
                    {
                        resizedStream.Seek(0, SeekOrigin.Begin);
                        await WriteS3Object(
                            System.Environment.GetEnvironmentVariable("ThumbnailBucketName", EnvironmentVariableTarget.Process),
                            $"thumb_{s3Event.Object.Key}",
                            resizedStream);
                    }
                }

                context.Logger.LogLine("Operation Complete");
            }
            catch (Exception ex)
            {
                context.Logger.LogLine(ex.Message);
            }
        }

        async Task<GetObjectResponse> GetS3Object(string bucketName, string keyName)
        {
            using (var client = new AmazonS3Client(RegionEndpoint.USEast1))
            {
                var request = new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName
                };

                ResponseHeaderOverrides responseHeaders = new ResponseHeaderOverrides();
                responseHeaders.CacheControl = "No-cache";
                request.ResponseHeaderOverrides = responseHeaders;

                return await client.GetObjectAsync(request);
            }
        }

        Stream GetResizedStream(Stream stream, decimal scalingFactor, string mimeType)
        {
            using (Image<Rgba32> image = Image.Load(stream))
            {
                var resizeOptions = new ResizeOptions
                {
                    Size = new SixLabors.Primitives.Size
                    {
                        Width = Convert.ToInt32(image.Width * scalingFactor),
                        Height = Convert.ToInt32(image.Height * scalingFactor)
                    },
                    Mode = ResizeMode.Stretch
                };

                image.Mutate(x => x.Resize(resizeOptions));

                var memoryStream = new MemoryStream();
                image.Save(memoryStream, mimeType.AsEncoder());

                return memoryStream;
            }
        }
        async Task<bool> WriteS3Object(string bucketName, string keyName, Stream contentStream)
        {
            using (var client = new AmazonS3Client(RegionEndpoint.USEast1))
            {
                var request = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = keyName,
                    InputStream = contentStream
                };

                await client.PutObjectAsync(request);
                return true;
            }
        }
    }
}
