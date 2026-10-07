# EEG Classification Model

This directory contains the machine learning model used by
SimpleBCI for EEG-based motor imagery classification.

## Source

The model is based on the following open-source project:

- Repository: https://github.com/vinayakr99/Muse-MotorImageryClassification/
- Author: vinayakr99
- License: MIT License

The original project implements a real-time left/right motor
imagery classifier using a Muse 2 EEG headband.

## Model

- Architecture: EEG-ITNet
- Framework: Keras
- Model format: ONNX
- Task: Left/Right Motor Imagery Classification
- EEG device: Muse 2
- EEG channels:
  - TP9
  - AF7
  - AF8
  - TP10

## Integration

The original model has been adapted for use with the SimpleBCI
library and ONNX Runtime.

The model is used locally for EEG classification and is not
trained as part of the SimpleBCI project.

## License

The original model and its source project are licensed under
the MIT License.

See the original repository and LICENSE file for the complete
license text:

https://github.com/vinayakr99/Muse-MotorImageryClassification/blob/main/LICENSE

When redistributing the model or derivative works, the original
copyright and license notice must be retained.