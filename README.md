## Bootstrap
Since this template utilizes bootstrap scss, the initial css file needs to be generated. There are two scripts included for doing this, one is sass-dev that will run the process in watch mode and the other is sass-prod that will compress the css file for production use. In order to do this perform the following steps in your terminal:

```
cd src/ContentManagementSystem.Server
npm run sass-dev (or sass-prod depending on which one you want)
```

## EF Migrations
This project adds EF as a dotnet tool, so before running any EF commands, one needs to run the following command from the project folder (there is also a command in the Aspire dashboard to run this restore command if the app is started before the restore command is run manually):

```
dotnet tool restore
```

After creating a new project with this template, migrations need to be run since there is some authentication that has been added. To do this, run the following command in your terminal:

```
cd src/ContentManagementSystem.Server
dotnet ef migrations add InitialDatabase -p ../ContentManagementSystem.Data
```

Migrations will run when the aspire project is started, so no need to run the migration manually after it is created.

## Aspire
This project is configured with aspire and should use the aspire cli, so the recommended way to kick off project execution is the following:

```
aspire run
```
