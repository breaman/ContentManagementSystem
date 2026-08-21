The goal here is to create a basic Content Management System. At a minimum the following features should be available for an admin/content editor
- Create templates that let them specify data zones
- Specify what type of data can be used in a zone (plain text, reusable content, html/markdown, etc)
- In zones that are plain text or html/mardown, it should allow for inline editing that would have an "edit/preview" editor experience
- Reusable content would just be html elements that are specified once but then reused in multiple (things like common footers, image carousels, etc)
- Administrators should be able to change the look and feel of the public site without a developer being involved. There should be a site-wide CSS file they can create and edit from an editor inside the system, applied on top of the styles the application already ships, and affecting the public facing pages only — not the admin screens

based on the templates that have been created, content editors should be able to create pages from those templates where they would then be able to populate the "placeholder" areas with actual content. Pages at a minimum would need to have a url specified so that end users would be able to navigate to the pages

Content editors should be able to have pages in draft mode before they get published out. pages should be versioned so that a published page could still be visible to unauthenticated users while content editors are making changes that only they can see internally

Content editors should also be able to have some sort of image management functionality. content editors should be able to upload images, resize and rotate those images, as well as then "reference" those images inside the pages they are creating.

I know there are plenty of other CMS systems out there and this is missing a lot of the functionality that those systems have, so do plenty of research and add elements that are clearly missing that would prevent this from being a usable system.