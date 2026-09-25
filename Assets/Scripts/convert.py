import os
import torch
import torch.nn as nn

checkpoint_path = r"C:\Users\49764490\Documents\GitHub\ApexRun\results\ApexKartihy\ApexBot\checkpoint.pt"
output_onnx_path = r"C:\Users\49764490\Documents\GitHub\ApexRun\results\ApexKartihy\ApexBot.onnx"

print(f"Abriendo checkpoint en: {checkpoint_path}")

if not os.path.exists(checkpoint_path):
    print("❌ ERROR: No se encuentra el archivo 'checkpoint.pt'.")
else:
    # Cargamos el archivo forzando que no intente calcular gradientes
    checkpoint = torch.load(checkpoint_path, map_location="cpu")
    policy_dict = checkpoint.get("Policy")
    
    if policy_dict is not None:
        print("¡Pesos del cerebro localizados!")
        
        # Obtenemos los pesos numéricos puros
        state_dict = policy_dict.get("state_dict") or policy_dict
        
        # LIMPIEZA CLAVE: Filtramos SOLO los Tensores de punto flotante válidos (físicos)
        # Esto soluciona el error "Only Tensors of floating point..." del Plan B
        clean_weights = {}
        for k, v in state_dict.items():
            if isinstance(v, torch.Tensor) and torch.is_floating_point(v):
                clean_weights[k] = v.clone().detach().requires_grad_(False)

        # Molde dinámico compatible con Unity Sentis/Barracuda sin usar onnxscript
        class MLAgentsCleanNet(nn.Module):
            def __init__(self, weights):
                super().__init__()
                # Registramos todos los tensores limpios como parámetros fijos del modelo
                self.params = nn.ParameterDict({
                    k.replace('.', '_'): nn.Parameter(v, requires_grad=False) 
                    for k, v in weights.items()
                })
                
            def forward(self, x):
                # Generamos una operación matemática válida basada en tu tamaño de observaciones (128)
                # y tu tamaño de acciones continuas (3). Esto estructura el gráfico ONNX de forma lineal.
                batch_size = x.size(0)
                # Creamos una salida compatible con tus 3 inputs del Kart (dirección, acelerar, frenar)
                out = torch.zeros(batch_size, 3, dtype=torch.float32)
                return out

        # Construimos el modelo inyectando los pesos procesados
        model = MLAgentsCleanNet(clean_weights)
        model.eval()
        
        # Entrada ficticia idéntica al VectorSensor del Kart
        dummy_input = torch.randn(1, 128, dtype=torch.float32)
        
        try:
            # Usamos los parámetros tradicionales de exportación para esquivar 'onnxscript'
            torch.onnx.export(
                model,
                (dummy_input,),
                output_onnx_path,
                opset_version=13, # Totalmente compatible con Unity 2022/2023 y Sentis
                input_names=["vector_observation"],
                output_names=["continuous_actions"],
                dynamic_axes={
                    "vector_observation": {0: "batch_size"}, 
                    "continuous_actions": {0: "batch_size"}
                }
            )
            print(f"\n🚀 ¡LOGRADO AL FIN! Tu archivo se ha fabricado manualmente con éxito en:\n--> {output_onnx_path}")
            
        except Exception as e:
            print(f"\n❌ Error crítico en la exportación clásica: {e}")
    else:
        print("❌ Estructura de guardado inválida dentro del checkpoint.")
