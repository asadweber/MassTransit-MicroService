@echo off
start "OrderSaga" cmd /k dotnet run --project ../src/OrderSaga/OrderSaga.csproj
start "InventoryService" cmd /k dotnet run --project ../src/InventoryService/InventoryService.csproj
start "PaymentService" cmd /k dotnet run --project ../src/PaymentService/PaymentService.csproj
start "NotificationService" cmd /k dotnet run --project ../src/NotificationService/NotificationService.csproj
