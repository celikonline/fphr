const DEFAULT_MAX_SIDE = 512;
const DEFAULT_QUALITY = 0.85;
const JPEG_TYPE = "image/jpeg";

/** Largest side after downsizing while keeping the aspect ratio; never enlarges a smaller image. */
export function fitWithin(
  width: number,
  height: number,
  maxSide: number
): { width: number; height: number } {
  const scale = Math.min(1, maxSide / Math.max(width, height));
  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

/**
 * Re-encodes a picked image as a small JPEG (default max 512 px): keeps every upload well under the API's 2 MB cap,
 * applies the EXIF orientation and strips metadata (location etc.) before the file leaves the browser.
 * Throws when the file cannot be decoded as an image.
 */
export async function resizeImageToJpeg(
  file: Blob,
  maxSide = DEFAULT_MAX_SIDE,
  quality = DEFAULT_QUALITY
): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: "from-image" });
  try {
    const { width, height } = fitWithin(bitmap.width, bitmap.height, maxSide);
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    if (!context) throw new Error("Canvas is not available");
    // JPEG has no alpha: paint white first so transparent PNGs do not turn black.
    context.fillStyle = "#ffffff";
    context.fillRect(0, 0, width, height);
    context.drawImage(bitmap, 0, 0, width, height);
    const blob = await new Promise<Blob | null>((resolve) =>
      canvas.toBlob(resolve, JPEG_TYPE, quality)
    );
    if (!blob) throw new Error("Image could not be encoded");
    return blob;
  } finally {
    bitmap.close();
  }
}
