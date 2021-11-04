/* eslint-disable @typescript-eslint/no-explicit-any */
import type { FC } from 'react'
import React, { Fragment } from 'react'
import { useRuntime } from 'vtex.render-runtime'
import {
  Layout,
  PageHeader,
  Card,
  Button,
  ButtonPlain,
  Spinner,
  Divider,
} from 'vtex.styleguide'
import { FormattedMessage, useIntl } from 'react-intl'
import { useQuery, useMutation } from 'react-apollo'

import GoogleSignIn from '../public/metadata/google_signin.png'
import Q_OWNER_EMAIL from './queries/GetOwnerEmail.gql'
import Q_HAVE_TOKEN from './queries/HaveToken.gql'
import Q_SHEET_LINK from './queries/SheetLink.gql'
import M_REVOKE from './mutations/RevokeToken.gql'
import M_CREATE_SHEET from './mutations/CreateSheet.gql'
import ProcessSheetButton from './components/ProcessSheetButton'
import ClearSheetButton from './components/ClearSheetButton'
import AddImagesButton from './components/AddImagesButton'

const AUTH_URL = '/sheets-catalog-import/auth'

const Admin: FC = () => {
  const { account, pages } = useRuntime()
  const intl = useIntl()

  const {
    loading: ownerLoading,
    called: ownerCalled,
    data: ownerData,
  } = useQuery(Q_OWNER_EMAIL, {
    variables: {
      accountName: account,
    },
  })

  const {
    loading: linkLoading,
    called: linkCalled,
    data: linkData,
  } = useQuery<Sheet>(Q_SHEET_LINK)

  const {
    loading: tokenLoading,
    called: tokenCalled,
    data: tokenData,
  } = useQuery<Token>(Q_HAVE_TOKEN)

  const [revoke, { loading: revokeLoading }] = useMutation(M_REVOKE, {
    onCompleted: (ret: any) => {
      if (ret.revokeToken === true) {
        window.location.reload()
      }
    },
  })

  const [
    create,
    { loading: createLoading, data: createData, called: createCalled },
  ] = useMutation(M_CREATE_SHEET)

  const auth = () => {
    revoke()
      .then(() => {
        window.top.location.href = AUTH_URL
      })
      .catch(() => {
        window.top.location.href = AUTH_URL
      })
  }

  const showLink = () => {
    return (
      (linkCalled && !linkLoading && linkData?.sheetLink) ||
      (createCalled && !createLoading && !!createData?.createSheet)
    )
  }

  return (
    <Layout
      pageHeader={
        <div className="flex justify-center">
          <div className="w-100 mw-reviews-header">
            <PageHeader
              title={intl.formatMessage({
                id: 'admin/sheets-catalog-import.title',
              })}
            >
              {tokenCalled && !tokenLoading && tokenData?.haveToken === true && (
                <div>
                  {ownerCalled && !ownerLoading && ownerData && (
                    <Fragment>
                      <FormattedMessage id="admin/sheets-catalog-import.connected-as" />{' '}
                      <strong>{`${ownerData.getOwnerEmail}`}</strong>
                    </Fragment>
                  )}
                  <div className="mt4 mb4 tr">
                    <Button
                      variation="danger-tertiary"
                      size="regular"
                      isLoading={revokeLoading}
                      onClick={() => {
                        revoke({
                          variables: {
                            accountName: account,
                          },
                        })
                      }}
                      collapseLeft
                    >
                      <FormattedMessage id="admin/sheets-catalog-import.disconnect.button" />
                    </Button>
                  </div>
                </div>
              )}
            </PageHeader>
          </div>
        </div>
      }
      fullWidth
    >
      {tokenCalled && (
        <div>
          {tokenLoading && (
            <div className="pv6">
              <Spinner />
            </div>
          )}
          {!tokenLoading && tokenData?.haveToken !== true && (
            <div>
              <Card>
                <h2>
                  <FormattedMessage id="admin/sheets-catalog-import.setup.title" />
                </h2>
                <p>
                  <FormattedMessage id="admin/sheets-catalog-import.setup.description" />{' '}
                  <div className="mt4">
                    <ButtonPlain
                      variation="primary"
                      collapseLeft
                      onClick={() => {
                        auth()
                      }}
                    >
                      <img src={GoogleSignIn} alt="Sign in with Google" />
                    </ButtonPlain>
                  </div>
                </p>
              </Card>
            </div>
          )}
        </div>
      )}
      {tokenCalled && !tokenLoading && tokenData?.haveToken === true && (
        <div className="bg-base pa8">
          <h2>
            <FormattedMessage id="admin/sheets-catalog-import.catalog-product.title" />
          </h2>

          {!createData && linkCalled && !linkLoading && !linkData?.sheetLink && (
            <Card>
              <div className="flex">
                <div className="w-70">
                  <p>
                    <FormattedMessage id="admin/sheets-catalog-import.create-sheet.description" />
                  </p>
                </div>

                <div
                  style={{ flexGrow: 1 }}
                  className="flex items-stretch w-20 justify-center"
                >
                  <Divider orientation="vertical" />
                </div>

                <div className="w-30 items-center flex">
                  <Button
                    variation="secondary"
                    collapseLeft
                    block
                    isLoading={createLoading}
                    onClick={() => {
                      create()
                    }}
                  >
                    <FormattedMessage id="admin/sheets-catalog-import.create-sheet.button" />
                  </Button>
                </div>
              </div>
            </Card>
          )}
          {showLink() && (
            <Card>
              <div className="flex">
                <div className="w-100">
                  <p>
                    <FormattedMessage id="admin/sheets-catalog-import.sheet-link.description" />{' '}
                    <a
                      href={createData?.createSheet || linkData?.sheetLink}
                      target="_blank"
                      rel="noreferrer"
                    >
                      {createData?.createSheet || linkData?.sheetLink}
                    </a>
                  </p>
                </div>
              </div>
            </Card>
          )}
          <br />
          {showLink() && <ProcessSheetButton />}
          <br />
          {showLink() && <ClearSheetButton />}
          <br />
          {pages['admin.app.google-drive-import'] && showLink() && (
            <AddImagesButton />
          )}
        </div>
      )}
    </Layout>
  )
}

export default Admin
