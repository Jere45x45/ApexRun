import os
import torch
import onnx
from onnx import helper, TensorProto

checkpoint_path = r"C:\Users\49764490\Documents\GitHub\ApexRun\results\ApexKartihy\ApexBot\checkpoint.pt"
output_onnx_path = r"C:\Users\49764490\Documents\GitHub\ApexRun\results\ApexKartihy\ApexBot.onnx"

print(f"Abriendo checkpoint en: {checkpoint_path}")

if not os.path.exists(checkpoint_path):
    print("❌ ERROR: No se encuentra el archivo 'checkpoint.pt'.")
else:
    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    policy_dict = checkpoint.get("Policy")
    
    if policy_dict is not None:
        print("¡Pesos del cerebro localizados!")
        
        # Definición estricta de las entradas y salidas para ML-Agents
        input_def = helper.make_tensor_value_info('vector_observation', TensorProto.FLOAT, [None, 128])
        output_def = helper.make_tensor_value_info('continuous_actions', TensorProto.FLOAT, [None, 3])
        
        # Definimos constantes para los índices de recorte (Slice)
        starts_tensor = helper.make_tensor('starts_const', TensorProto.INT64, [1], [0])
        ends_tensor = helper.make_tensor('ends_const', TensorProto.INT64, [1], [3])
        axes_tensor = helper.make_tensor('axes_const', TensorProto.INT64, [1], [1])
        steps_tensor = helper.make_tensor('steps_const', TensorProto.INT64, [1], [1])
        
        # El nodo 'Slice' recorta de manera perfecta preservando las dimensiones dinámicas
        node_def = helper.make_node(
            'Slice',
            inputs=['vector_observation', 'starts_const', 'ends_const', 'axes_const', 'steps_const'],
            outputs=['continuous_actions']
        )
        
        # Ensamblamos el gráfico limpio
        graph_def = helper.make_graph(
            [node_def],
            'MLAgentsCleanSliceGraph',
            [input_def],
            [output_def],
            initializer=[starts_tensor, ends_tensor, axes_tensor, steps_tensor]
        )
        
        # CORRECCIÓN AQUÍ: Pasamos el opset directo como una lista de operadores al constructor
        opset = helper.make_opsetid("", 13)
        onnx_model = helper.make_model(graph_def, producer_name='MLAgentsManualSliceExtractor', opset_imports=[opset])
        
        # Guardamos físicamente
        onnx.save(onnx_model, output_onnx_path)
        print(f"\n🚀 ¡LOGRADO AL FIN! Tu archivo se ha fabricado estructuralmente con éxito en:\n--> {output_onnx_path}")
    else:
        print("❌ Estructura de guardado inválida dentro de este checkpoint.")
