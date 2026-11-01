"""Converts MediaPipe's pose models to Rockchip NPU models (RKNN): the person detector and the lite / full landmark
models, taken out of motion-service-py/models/pose_landmarker_<lite|full>.task (a zip), for one chip (rk3566 = RK3566
and RK3568; RK3576 / RK3588 need their own build). Float16, no quantization (no calibration pictures needed, the same
numbers as MediaPipe's GPU path). The inputs become uint8 RGB with the normalization inside the model: the detector
(x - 127.5) / 127.5, the landmarks x / 255.

--quant builds INT8 instead (<name>.<platform>.int8.rknn, calibrated on calibration.py's pictures; pose_npu.cpp runs
them, PoseEngine doesn't pick them). On the RK3566 (2026-11-01) INT8 halved the NPU time but was far less accurate:
landmarks ~12 px off the original model where float16 is 0.3 px, the detector finding people float16 doesn't (some of
them cushions). docs/hardware.md "INT8 pose models".

Needs Rockchip's RKNN-Toolkit2 (Linux x86-64 only). On the laptop it lives in WSL, on D: (C: is full):
    wsl -d Ubuntu -- bash -lc ". /mnt/d/rknn/venv/bin/activate && cd /mnt/d/Projects/moveit && python tools/npu/convert.py"
The runtime on the box must match the toolkit's version (2.3.2: MoveItMotion.androidlib/src/main/jniLibs).
"""
import argparse
import os
import shutil
import sys
import tempfile
import zipfile

TOOLKIT = '2.3.2'
SOURCES = {'detector': 'pose_detector.tflite', 'landmarks': 'pose_landmarks_detector.tflite'}
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..')


# INT8 (--quant): RKNN's w8a8 (8-bit weights per channel and activations per tensor), the ranges calibrated on pictures
# of what each model really sees (calibration.py), with RKNN's way of choosing the ranges. On the RK3566 'normal' was the
# least bad; 'mmse' and 'kl' clip the outputs' ranges (world joints 16 and 135 cm off). 'w8a16' (16-bit activations)
# isn't available for the RK3566 (the toolkit refuses it), maybe for a newer chip.
QUANT = {
    'normal': dict(quantized_dtype='w8a8', quantized_algorithm='normal', quantized_method='channel'),
    'mmse': dict(quantized_dtype='w8a8', quantized_algorithm='mmse', quantized_method='channel'),
    'kl': dict(quantized_dtype='w8a8', quantized_algorithm='kl_divergence', quantized_method='channel'),
    'w8a16': dict(quantized_dtype='w8a16', quantized_algorithm='normal', quantized_method='channel'),
}


def configure(rknn, name, platform='rk3566', quant=None):
    mean, std = (127.5, 127.5) if name == 'detector' else (0.0, 255.0)
    rknn.config(mean_values=[[mean] * 3], std_values=[[std] * 3], target_platform=platform, optimization_level=3,
                **(QUANT[quant] if quant else {}))


def source(folder, model, name):
    """The TFLite file inside an unpacked .task folder (<folder>/<model>/...)."""
    return os.path.join(folder, model, SOURCES[name])


def unpack(models_dir, into):
    for model in ('lite', 'full'):
        with zipfile.ZipFile(os.path.join(models_dir, f'pose_landmarker_{model}.task')) as z:
            z.extractall(os.path.join(into, model))


def litert_interpreter():
    """The toolkit reads the TFLite model with TensorFlow's interpreter (2.14 at most), which crashes listing the
    tensors of MediaPipe's newer models (segfault in get_tensor_details, with or without XNNPACK). Google's current
    TFLite runtime (LiteRT, pip ai-edge-litert) reads them: hand the toolkit that one instead."""
    import tensorflow as tf
    from ai_edge_litert.interpreter import Interpreter
    if tf.lite.Interpreter is Interpreter:
        return
    sys.modules[tf.lite.Interpreter.__module__].Interpreter = Interpreter   # tensorflow/lite/python/interpreter.py
    tf.lite.Interpreter = Interpreter


def densify(src, dst):
    """MediaPipe's person detector keeps most weights sparse (DENSIFY ops in front of the convolutions), which the
    toolkit's TFLite reader can't parse. Write a copy with those weights stored dense: LiteRT computes every DENSIFY
    output once, the output tensor gets that data as a constant buffer, and the op goes."""
    import numpy as np
    from ai_edge_litert.interpreter import Interpreter, OpResolverType
    from tensorflow.lite.python import schema_py_generated as schema
    from tensorflow.lite.tools import flatbuffer_utils

    model = flatbuffer_utils.read_model(src)
    graph = model.subgraphs[0]
    densify_ops = [op for op in graph.operators
                   if max(model.operatorCodes[op.opcodeIndex].builtinCode,
                          model.operatorCodes[op.opcodeIndex].deprecatedBuiltinCode) == schema.BuiltinOperator.DENSIFY]
    if not densify_ops:
        shutil.copy(src, dst)
        return
    # without XNNPACK: it takes the DENSIFY ops into its own kernels and never writes their outputs
    it = Interpreter(model_path=src, experimental_preserve_all_tensors=True,
                     experimental_op_resolver_type=OpResolverType.BUILTIN_WITHOUT_DEFAULT_DELEGATES)
    it.allocate_tensors()
    d = it.get_input_details()[0]
    it.set_tensor(d['index'], np.zeros(d['shape'], d['dtype']))
    it.invoke()
    for op in densify_ops:
        sparse, out = op.inputs[0], op.outputs[0]
        buffer = schema.BufferT()
        buffer.data = np.frombuffer(it.get_tensor(out).tobytes(), np.uint8)
        model.buffers.append(buffer)
        graph.tensors[out].buffer = len(model.buffers) - 1
        graph.operators.remove(op)
        graph.tensors[sparse].buffer = 0          # the sparse original, now unused: no data (buffer 0 is empty)
        graph.tensors[sparse].sparsity = None
    flatbuffer_utils.write_model(model, dst)
    print(f'{os.path.basename(src)}: {len(densify_ops)} sparse weights stored dense')


def prune(src, dst, drop_shapes):
    """A copy without the outputs of these shapes and every operation only they need. The landmark model's person
    segmentation (a 256 x 256 mask scaled up on the NPU) was ~55 of its ~96 ms on the RK3566, and we don't use it."""
    from tensorflow.lite.tools import flatbuffer_utils
    model = flatbuffer_utils.read_model(src)
    graph = model.subgraphs[0]
    graph.outputs = [o for o in graph.outputs if tuple(graph.tensors[o].shape) not in drop_shapes]
    needed = set(graph.outputs)
    kept = []
    for op in reversed(graph.operators):
        if any(o in needed for o in op.outputs):
            kept.append(op)
            needed.update(i for i in op.inputs if i >= 0)
    dropped = len(graph.operators) - len(kept)
    graph.operators = list(reversed(kept))
    flatbuffer_utils.write_model(model, dst)
    print(f'{os.path.basename(src)}: {dropped} operations only for {sorted(drop_shapes)} removed')


def channels_first(src, dst, shape):
    """A copy whose output of this NHWC shape comes out NCHW (a TRANSPOSE appended). The NPU computes channels first,
    so the toolkit had appended a transpose back to NHWC that the box ran on its CPU (~10 ms for the 64 x 64 x 39
    heatmap); the two transposes cancel out in the toolkit's optimizer."""
    import numpy as np
    from tensorflow.lite.python import schema_py_generated as schema
    from tensorflow.lite.tools import flatbuffer_utils
    model = flatbuffer_utils.read_model(src)
    graph = model.subgraphs[0]
    index = next(o for o in graph.outputs if tuple(graph.tensors[o].shape) == shape)
    codes = [max(c.builtinCode, c.deprecatedBuiltinCode) for c in model.operatorCodes]
    if schema.BuiltinOperator.TRANSPOSE not in codes:
        code = schema.OperatorCodeT()
        code.builtinCode = code.deprecatedBuiltinCode = schema.BuiltinOperator.TRANSPOSE
        code.version = 1
        model.operatorCodes.append(code)
        codes.append(schema.BuiltinOperator.TRANSPOSE)

    def tensor(name, dims, dtype, data=None):
        buffer = schema.BufferT()
        buffer.data = None if data is None else np.frombuffer(data.tobytes(), np.uint8)
        model.buffers.append(buffer)
        t = schema.TensorT()
        t.name, t.shape, t.type, t.buffer = name, np.array(dims, np.int32), dtype, len(model.buffers) - 1
        graph.tensors.append(t)
        return len(graph.tensors) - 1

    perm = [0, 3, 1, 2]
    source = graph.tensors[index]
    name = source.name.decode() if isinstance(source.name, bytes) else source.name
    perm_index = tensor(f'{name}_to_nchw_perm', [4], schema.TensorType.INT32, np.array(perm, np.int32))
    out_index = tensor(f'{name}_nchw', [shape[p] for p in perm], source.type)
    op = schema.OperatorT()
    op.opcodeIndex = codes.index(schema.BuiltinOperator.TRANSPOSE)
    op.inputs, op.outputs = [index, perm_index], [out_index]
    op.builtinOptionsType = schema.BuiltinOptions.TransposeOptions
    op.builtinOptions = schema.TransposeOptionsT()
    graph.operators.append(op)
    graph.outputs = [out_index if o == index else o for o in graph.outputs]
    flatbuffer_utils.write_model(model, dst)


SEGMENTATION = (1, 256, 256, 1)
HEATMAP = (1, 64, 64, 39)


def prepare(src, name):
    """The TFLite file the toolkit can read and the NPU runs well: the detector with its sparse weights stored dense,
    the landmark models without the segmentation mask and with the heatmap channels first."""
    litert_interpreter()
    if name == 'detector':
        dense = src.replace('.tflite', '.dense.tflite')
        densify(src, dense)
        return dense
    pruned = src.replace('.tflite', '.pruned.tflite')
    prune(src, pruned, {SEGMENTATION})
    ready = src.replace('.tflite', '.npu.tflite')
    channels_first(pruned, ready, HEATMAP)
    return ready


def make(src, name, platform='rk3566', quant=None, dataset=None):
    """The model built by the toolkit (export it, or run it in the simulator): float16, or INT8 calibrated on the
    pictures listed in dataset (a calibration.py list)."""
    src = prepare(src, name)
    from rknn.api import RKNN
    rknn = RKNN(verbose=False)
    configure(rknn, name, platform, quant)
    if rknn.load_tflite(model=src) != 0:
        raise SystemExit(f'load failed: {src}')
    if rknn.build(do_quantization=bool(quant), dataset=dataset) != 0:
        raise SystemExit(f'build failed: {src}')
    return rknn


def build(src, dst, name, platform, quant=None, dataset=None):
    rknn = make(src, name, platform, quant, dataset)
    if rknn.export_rknn(dst) != 0:
        raise SystemExit(f'export failed: {dst}')
    rknn.release()
    print(f'{dst}: {os.path.getsize(dst) / 1e6:.1f} MB')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--models', default=os.path.join(ROOT, 'motion-service-py', 'models'))
    ap.add_argument('--out', default=None, help='where the .rknn files go (default: --models)')
    ap.add_argument('--platform', default='rk3566')
    ap.add_argument('--keep', default=None, help='also keep the unpacked TFLite files in this folder')
    ap.add_argument('--only', default='detector,lite,full', help='which of detector, lite, full to build')
    ap.add_argument('--quant', default=None, choices=sorted(QUANT),
                    help='INT8 (<name>.<platform>.int8.rknn) instead of float16, calibrated on --calibration')
    ap.add_argument('--calibration', default=None, help='--quant: the folder calibration.py wrote')
    args = ap.parse_args()
    if args.quant and not args.calibration:
        ap.error('--quant needs --calibration')
    work = args.keep or tempfile.mkdtemp()
    unpack(args.models, work)
    suffix = '.int8' if args.quant else ''
    out = lambda n: os.path.join(args.out or args.models, f'{n}.{args.platform}{suffix}.rknn')  # noqa: E731
    dataset = lambda n: args.calibration and os.path.join(args.calibration, f'{n}.txt')  # noqa: E731
    only = args.only.split(',')
    if 'detector' in only:
        build(source(work, 'lite', 'detector'), out('pose_detector'), 'detector', args.platform, args.quant,
              dataset('detector'))
    for model in ('lite', 'full'):
        if model in only:
            build(source(work, model, 'landmarks'), out(f'pose_landmarks_{model}'), 'landmarks', args.platform,
                  args.quant, dataset(f'landmarks_{model}'))
    if not args.keep:
        shutil.rmtree(work)


if __name__ == '__main__':
    main()
