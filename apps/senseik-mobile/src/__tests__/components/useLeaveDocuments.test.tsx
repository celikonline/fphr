import React from "react";
import { Alert } from "react-native";
import { act, create } from "react-test-renderer";
import * as DocumentPicker from "expo-document-picker";
import * as ImagePicker from "expo-image-picker";
import { isDocumentFileSizeAllowed, uploadMyDocument } from "@/services/DocumentService";
import { buildCapturedPhotoFileName } from "@/components/leave/leaveRequestRules";
import { useLeaveDocuments } from "@/components/leave/useLeaveDocuments";

jest.mock("expo-document-picker", () => ({
  getDocumentAsync: jest.fn(),
}));
jest.mock("expo-image-picker", () => ({
  requestCameraPermissionsAsync: jest.fn(),
  launchCameraAsync: jest.fn(),
}));
jest.mock("@/services/DocumentService", () => ({
  isDocumentFileSizeAllowed: jest.fn(),
  uploadMyDocument: jest.fn(),
}));

const mockedGetDocumentAsync = DocumentPicker.getDocumentAsync as jest.Mock;
const mockedRequestCameraPermissionsAsync = ImagePicker.requestCameraPermissionsAsync as jest.Mock;
const mockedLaunchCameraAsync = ImagePicker.launchCameraAsync as jest.Mock;
const mockedIsDocumentFileSizeAllowed = isDocumentFileSizeAllowed as jest.Mock;
const mockedUploadMyDocument = uploadMyDocument as jest.Mock;

type Hook = ReturnType<typeof useLeaveDocuments>;

let latest: Hook | null = null;

function Probe({ leaveTypeName }: { leaveTypeName: string | undefined }) {
  latest = useLeaveDocuments(leaveTypeName);
  return null;
}

async function mount(leaveTypeName: string | undefined = "İzin Türü") {
  await act(async () => {
    create(<Probe leaveTypeName={leaveTypeName} />);
  });
}

beforeEach(async () => {
  jest.clearAllMocks();
  latest = null;
  jest.spyOn(Alert, "alert").mockImplementation(() => {});
  await mount();
});

describe("useLeaveDocuments (B-mobil-004)", () => {
  it("shows the 'Dosya yüklenemedi' alert and skips the upload when isDocumentFileSizeAllowed is false", async () => {
    mockedGetDocumentAsync.mockResolvedValue({
      canceled: false,
      assets: [
        { uri: "file://big.pdf", name: "big.pdf", mimeType: "application/pdf", size: 999999999 },
      ],
    });
    mockedIsDocumentFileSizeAllowed.mockReturnValue(false);

    await act(async () => {
      await latest?.pickDocumentFile();
    });

    expect(Alert.alert).toHaveBeenCalledWith(
      "Dosya yüklenemedi",
      "Dosya boyutu okunamadı ya da 100 MB sınırını aşıyor. Lütfen başka bir dosya seçin."
    );
    expect(mockedUploadMyDocument).not.toHaveBeenCalled();
    expect(latest?.documents).toEqual([]);
    expect(latest?.uploadingDocument).toBe(false);
  });

  it("shows the 'Kamera izni gerekli' alert and never calls launchCameraAsync when camera permission is denied", async () => {
    mockedRequestCameraPermissionsAsync.mockResolvedValue({ granted: false });

    await act(async () => {
      await latest?.captureDocumentPhoto();
    });

    expect(Alert.alert).toHaveBeenCalledWith(
      "Kamera izni gerekli",
      "Belge fotoğrafı çekebilmek için ayarlardan kamera iznini açmanız gerekir."
    );
    expect(mockedLaunchCameraAsync).not.toHaveBeenCalled();
    expect(mockedUploadMyDocument).not.toHaveBeenCalled();
  });

  it("stages the uploaded document with the buildCapturedPhotoFileName-generated name on a successful camera capture", async () => {
    jest.useFakeTimers();
    jest.setSystemTime(new Date(2026, 8, 28, 13, 24, 0));
    const expectedName = buildCapturedPhotoFileName(
      "İzin Türü",
      new Date(2026, 8, 28, 13, 24, 0),
      "image/jpeg"
    );
    mockedRequestCameraPermissionsAsync.mockResolvedValue({ granted: true });
    mockedLaunchCameraAsync.mockResolvedValue({
      canceled: false,
      assets: [{ uri: "file://photo.jpg", mimeType: "image/jpeg", fileSize: 12345 }],
    });
    mockedIsDocumentFileSizeAllowed.mockReturnValue(true);
    mockedUploadMyDocument.mockResolvedValue("doc-1");

    await act(async () => {
      await latest?.captureDocumentPhoto();
    });

    expect(mockedUploadMyDocument).toHaveBeenCalledWith({
      uri: "file://photo.jpg",
      name: expectedName,
      mimeType: "image/jpeg",
      size: 12345,
    });
    expect(latest?.documents).toEqual([{ id: "doc-1", name: expectedName }]);
    expect(latest?.uploadingDocument).toBe(false);
    jest.useRealTimers();
  });

  it("shows the 'Hata' alert with getApiErrorMessage's fallback and resets uploadingDocument when uploadMyDocument rejects", async () => {
    mockedGetDocumentAsync.mockResolvedValue({
      canceled: false,
      assets: [{ uri: "file://doc.pdf", name: "doc.pdf", mimeType: "application/pdf", size: 500 }],
    });
    mockedIsDocumentFileSizeAllowed.mockReturnValue(true);
    mockedUploadMyDocument.mockRejectedValue(new Error("boom"));

    await act(async () => {
      await latest?.pickDocumentFile();
    });

    expect(Alert.alert).toHaveBeenCalledWith("Hata", "Belge yüklenemedi. Lütfen tekrar deneyin.");
    expect(latest?.uploadingDocument).toBe(false);
    expect(latest?.documents).toEqual([]);
  });

  it("removeDocument and clearDocuments update the staged list correctly", async () => {
    mockedGetDocumentAsync
      .mockResolvedValueOnce({
        canceled: false,
        assets: [{ uri: "file://a.pdf", name: "a.pdf", mimeType: "application/pdf", size: 100 }],
      })
      .mockResolvedValueOnce({
        canceled: false,
        assets: [{ uri: "file://b.pdf", name: "b.pdf", mimeType: "application/pdf", size: 200 }],
      });
    mockedIsDocumentFileSizeAllowed.mockReturnValue(true);
    mockedUploadMyDocument.mockResolvedValueOnce("doc-a").mockResolvedValueOnce("doc-b");

    await act(async () => {
      await latest?.pickDocumentFile();
    });
    await act(async () => {
      await latest?.pickDocumentFile();
    });
    expect(latest?.documents).toEqual([
      { id: "doc-a", name: "a.pdf" },
      { id: "doc-b", name: "b.pdf" },
    ]);

    act(() => {
      latest?.removeDocument("doc-a");
    });
    expect(latest?.documents).toEqual([{ id: "doc-b", name: "b.pdf" }]);

    act(() => {
      latest?.clearDocuments();
    });
    expect(latest?.documents).toEqual([]);
  });
});
