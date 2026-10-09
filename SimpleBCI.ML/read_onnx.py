import onnx

path = r"C:/Users/Riley/Documents/Project Work/Training/Model/Model.onnx"
model = onnx.load(path)

for output in model.graph.output:
    dims = [
        dim.dim_value if dim.dim_value else dim.dim_param
        for dim in output.type.tensor_type.shape.dim
    ]
    print(output.name, dims)


import onnxruntime as ort

session = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
for output in session.get_outputs():
    print(output.name, output.shape, output.type)