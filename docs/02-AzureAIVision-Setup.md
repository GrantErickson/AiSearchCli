# Azure AI Vision — Setup Instructions

Azure AI Vision (Computer Vision) provides the multimodal embeddings API (Florence model) that generates 1024-dimensional vectors for both images and text in a shared embedding space.

## 1. Create the Computer Vision Resource

1. In the [Azure Portal](https://portal.azure.com), click **Create a resource**.
2. Search for **Computer Vision** → click **Create**.
3. Fill in:
   - **Subscription**: Your subscription.
   - **Resource group**: Same resource group as your AI Search (e.g., `rg-aisearch`).
   - **Region**: **Must** support multimodal embeddings. Supported regions include:
     - `East US`
     - `West US`
     - `West Europe`
     - `Japan East`
     - `Southeast Asia`
     - `Korea Central`
     - `North Central US`
     - `France Central`
     - `Australia East`
     - `Sweden Central`
       See [regional availability](https://learn.microsoft.com/en-us/azure/ai-services/computer-vision/how-to/image-retrieval) for the latest list.
   - **Name**: A unique name (e.g., `aivision-demo-001`).
   - **Pricing tier**: **Free (F0)** (20 calls/minute, 5,000/month) or **Standard (S1)** for higher throughput.
4. Click **Review + create** → **Create**.

## 2. Note Down Connection Information

Once deployed, go to the resource and record:

| Value        | Where to Find                                                                 |
| ------------ | ----------------------------------------------------------------------------- |
| **Endpoint** | Overview page (e.g., `https://aivision-demo-001.cognitiveservices.azure.com`) |
| **API Key**  | Resource Management → Keys and Endpoint → Key 1                               |

## 3. API Endpoints Used by the Application

No additional model deployment is needed — the Florence multimodal model is built into the Computer Vision resource.

### Vectorize an Image

```
POST {endpoint}/computervision/retrieval:vectorizeImage?api-version=2024-02-01&model-version=2023-04-15
Content-Type: application/octet-stream
Ocp-Apim-Subscription-Key: {api-key}

<binary image data>
```

### Vectorize Text

```
POST {endpoint}/computervision/retrieval:vectorizeText?api-version=2024-02-01&model-version=2023-04-15
Content-Type: application/json
Ocp-Apim-Subscription-Key: {api-key}

{
  "text": "text to vectorize"
}
```

### Response Format (both endpoints)

```json
{
  "modelVersion": "2023-04-15",
  "vector": [0.012, -0.034, 0.056, ... ]
}
```

The vector is an array of 1024 floating-point numbers.

## 4. Supported Image Formats

The vectorizeImage endpoint supports: JPEG, PNG, BMP, GIF, and TIFF.

Maximum image size: 20 MB (our app limits to 5 MB).

## 5. Free Tier Limits

| Limit                   | Value |
| ----------------------- | ----- |
| Transactions per minute | 20    |
| Transactions per month  | 5,000 |

Each vectorize call (image or text) counts as 1 transaction.

## 6. Values Required for `appsettings.json`

```json
{
  "AzureAIVision": {
    "Endpoint": "https://<your-vision-resource-name>.cognitiveservices.azure.com",
    "ApiKey": "<key-1>"
  }
}
```
