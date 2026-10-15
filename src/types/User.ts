export default interface User {
  id: number | string;
  userName: string;
  name: string;
  lastName: string;
  email: string;
  roleIds: any;
  roles?: any;
  password: string;
  confirmPassword: string;
  phoneNumber: string;
  birthDate: string;
  nationalCode: string;
}
