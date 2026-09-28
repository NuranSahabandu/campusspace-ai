import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// A photo the technician took or chose, held as bytes: the thumbnail is an Image.memory and the upload a
/// MultipartFile.fromBytes, so tests never touch the file system.
class PickedPhoto {
  const PickedPhoto({required this.bytes, required this.name});

  final Uint8List bytes;
  final String name;
}

enum PhotoSource { camera, gallery }

/// Takes or chooses a photo. Null when the user backs out. Behind [photoPickerProvider] so tests can fake it.
abstract class PhotoPicker {
  Future<PickedPhoto?> pick(PhotoSource source);
}

/// image_picker: the camera app or the system photo picker. The resize and re-compress keep a phone photo well
/// under the server's 5 MB limit.
class ImagePickerPhotoPicker implements PhotoPicker {
  static const maxWidth = 1600.0;
  static const imageQuality = 80;

  final _picker = ImagePicker();

  @override
  Future<PickedPhoto?> pick(PhotoSource source) async {
    final file = await _picker.pickImage(
      source: source == PhotoSource.camera ? ImageSource.camera : ImageSource.gallery,
      maxWidth: maxWidth,
      imageQuality: imageQuality,
    );
    if (file == null) return null;
    return PickedPhoto(bytes: await file.readAsBytes(), name: file.name);
  }
}

final photoPickerProvider = Provider<PhotoPicker>((ref) => ImagePickerPhotoPicker());
