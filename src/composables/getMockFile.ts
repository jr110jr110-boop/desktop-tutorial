export const getMockFile = (
  name = "mock.txt",
  size = 100,
  mimeType = "plain/txt"
) => {
  function range(count) {
    let output = "";
    for (let i = 0; i < count; i++) {
      output += "a";
    }
    return output;
  }

  const blob: any = new Blob([range(size)], { type: mimeType });
  blob.lastModifiedDate = new Date();
  blob.name = name;

  return blob;
};
