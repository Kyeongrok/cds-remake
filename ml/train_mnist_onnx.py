"""Train a MNIST CNN with augmentation for game pixel-font digit recognition."""
import torch
import torch.nn as nn
import torch.optim as optim
from torchvision import datasets, transforms
from torch.utils.data import DataLoader
import os, sys

class DigitCNN(nn.Module):
    def __init__(self):
        super().__init__()
        self.features = nn.Sequential(
            nn.Conv2d(1, 32, 3, padding=1), nn.BatchNorm2d(32), nn.ReLU(),
            nn.Conv2d(32, 32, 3, padding=1), nn.ReLU(), nn.MaxPool2d(2), nn.Dropout2d(0.25),
            nn.Conv2d(32, 64, 3, padding=1), nn.BatchNorm2d(64), nn.ReLU(),
            nn.Conv2d(64, 64, 3, padding=1), nn.ReLU(), nn.MaxPool2d(2), nn.Dropout2d(0.25),
        )
        self.classifier = nn.Sequential(
            nn.Flatten(), nn.Linear(64*7*7, 128), nn.ReLU(), nn.Dropout(0.5), nn.Linear(128, 10),
        )
    def forward(self, x):
        return self.classifier(self.features(x))

def main():
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f'Device: {device}', flush=True)

    train_transform = transforms.Compose([
        transforms.RandomAffine(degrees=10, translate=(0.1, 0.1), scale=(0.8, 1.2)),
        transforms.ToTensor(),
        transforms.Normalize((0.5,), (0.5,))
    ])
    test_transform = transforms.Compose([
        transforms.ToTensor(),
        transforms.Normalize((0.5,), (0.5,))
    ])

    print('Loading data...', flush=True)
    train_data = datasets.MNIST('C:/Users/ocean/Desktop/mnist_data', train=True, download=False, transform=train_transform)
    test_data = datasets.MNIST('C:/Users/ocean/Desktop/mnist_data', train=False, download=False, transform=test_transform)
    train_loader = DataLoader(train_data, batch_size=512, shuffle=True, num_workers=0)
    test_loader = DataLoader(test_data, batch_size=1000, num_workers=0)

    model = DigitCNN().to(device)
    optimizer = optim.Adam(model.parameters(), lr=0.001)
    scheduler = optim.lr_scheduler.StepLR(optimizer, step_size=5, gamma=0.5)
    criterion = nn.CrossEntropyLoss()

    print('Training...', flush=True)
    best_acc = 0
    for epoch in range(15):
        model.train()
        total_loss = 0
        for data, target in train_loader:
            data, target = data.to(device), target.to(device)
            optimizer.zero_grad()
            loss = criterion(model(data), target)
            loss.backward()
            optimizer.step()
            total_loss += loss.item()
        scheduler.step()

        model.eval()
        correct = total = 0
        with torch.no_grad():
            for data, target in test_loader:
                data, target = data.to(device), target.to(device)
                correct += (model(data).argmax(1) == target).sum().item()
                total += target.size(0)
        acc = 100*correct/total
        tag = ' *' if acc > best_acc else ''
        if acc > best_acc: best_acc = acc
        print(f'Epoch {epoch+1:2d}: loss={total_loss/len(train_loader):.4f}, acc={acc:.2f}%{tag}', flush=True)

    print(f'\nBest: {best_acc:.2f}%', flush=True)

    model.eval().cpu()
    dummy = torch.randn(1, 1, 28, 28)
    onnx_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'mnist_digit.onnx')
    torch.onnx.export(model, dummy, onnx_path,
        input_names=['input'], output_names=['output'],
        dynamic_axes={'input': {0: 'batch'}, 'output': {0: 'batch'}}, opset_version=13)
    print(f'Saved: {onnx_path} ({os.path.getsize(onnx_path)/1024:.1f} KB)', flush=True)

if __name__ == '__main__':
    main()
