using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Application.Shared.Options;
using Application.Shared.Services.Abstract;
using Domain.AttachmentManagment;
using Domain.AttachmentManagment.Enums;
using Domain.AttachmentManagment.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text;

namespace Application.Shared.Services.Concrete
{
    /// <summary>
    /// S3FileService
    /// </summary>
    public class S3FileService : IFileUploadService
    {
        private readonly IAmazonS3 client;
        private readonly IAttachmentRepository attachmentRepository;
        private readonly string bucket;

        /// <summary>
        /// Initializes a new instance of the <see cref="S3FileService"/> class.
        /// </summary>
        /// <param name="client">client.</param>
        /// <param name="attachmentRepository">attachmentRepository.</param>
        /// <param name="options">options.</param>
        public S3FileService(IAmazonS3 client, IAttachmentRepository attachmentRepository, IOptions<FileUploadOptions> options)
        {
            this.client = client;
            this.attachmentRepository = attachmentRepository;
            this.bucket = options.Value.Bucket;
        }

        /// <inheritdoc/>
        public async Task UploadAsync(string fileId, string fileName, Stream content, string folder, CancellationToken cancellationToken)
        {
            // max dim 100 mega
            using var fileTransferUtility = new TransferUtility(this.client);

            var request = new TransferUtilityUploadRequest()
            {
                InputStream = content,
                BucketName = this.GetBucketName(folder),
                Key = fileId,
            };

            await fileTransferUtility.UploadAsync(request, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task UploadAsync(IFormFile file, AttachmentCategory category, string? entityId, CancellationToken cancellationToken)
        {
            if (file != null && file.Length > 0)
            {
                var fileName = file.FileName;
                var fileExtension = Path.GetExtension(fileName);

                var attachment = new Attachment(fileName, fileExtension, category, entityId);
                await this.attachmentRepository.InsertAsync(attachment);

                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    await this.UploadAsync(attachment.FileId, attachment.FileName, reader.BaseStream, attachment.Folder, cancellationToken);
                }
            }
        }

        /// <inheritdoc/>
        public string GetUrl(string fileId, string fileName, string folder)
        {
            return this.client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = this.GetBucketName(folder),
                Key = fileId,
                Expires = DateTime.Now.AddMinutes(30),
                ResponseHeaderOverrides = new ResponseHeaderOverrides
                {
                    ContentDisposition = $"attachment; filename=\"{fileName}\"",
                },
            });
        }

        /// <inheritdoc/>
        public string GetUrl(AttachmentCategory category, string entityId)
        {
            var attachement = this.attachmentRepository.Query(x => x.AttachmentCategory == category && x.EntityId == entityId).FirstOrDefault();

            if (attachement == null)
            {
                return string.Empty;
            }

            return this.GetUrl(attachement.FileId, attachement.FileName, attachement.Folder);
        }

        /// <inheritdoc/>
        public async Task<(string KeyName, byte[] Content)> GetContentAsync(string fileId, string folder, CancellationToken cancellationToken)
        {
            using var fileTransferUtility = new TransferUtility(this.client);

            var fs = await fileTransferUtility.OpenStreamAsync(this.GetBucketName(folder), fileId, cancellationToken);
            using var memoryStream = new MemoryStream();
            fs.CopyTo(memoryStream);

            return new (fileId, memoryStream.ToArray());
        }

        private string GetBucketName(string folder)
        {
            var buckerNameBuilder = new StringBuilder();

            buckerNameBuilder.Append(this.bucket);

            if (!string.IsNullOrEmpty(folder))
            {
                buckerNameBuilder.Append("/");
                buckerNameBuilder.Append(folder);
            }

            return buckerNameBuilder.ToString();
        }
    }
}
