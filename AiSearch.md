# Azure AI Search

Goal: User can add, remove and search files from a command line app. Supports multi-modal files like text, html, pdf, jpg, etc.

The app should calculate the embeddings using Azure resources, push the index entry, and upload the file to blob storage.

## This should require

1. An Azure AI Search instance
2. A Multi-modal Azure embedding model deployment
3. Azure blob storage for storing files. This can be publicly accessible for read, but require a key to add files.

## Create the following:

1. Instructions for creating the Azure AI Search via the Azure Portal. This should include all requisite JSON definitions including the index fields and anything else necessary. The index should contain not only the vectors, but also any information about the file like URL, name, type, size and other things easily accessible or computable.
2. Instructions for creating the appropriate embeddings models via the Azure Portal.
3. Instructions for creating the blob storage account and container via the Azure Portal.
4. C# command line application that will

- Take a local file: determine the embeddings and push an entry for the file with embeddings to the index. Store the file in Azure Blob Storage.
- Take a text query: return the top 10 matches in the index.
- Take a command to remove a file from the index and Blob Storage.

5. Include any permissions that need to be set.
6. Include a configuration file that has all names and keys to be added clearly indicated.
